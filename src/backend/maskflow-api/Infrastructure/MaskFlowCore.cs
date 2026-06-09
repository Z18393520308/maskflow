using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

public static class Util
{
    public static string Id() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed class MaskFlowStore
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly IMaskFlowRepository repository;
    readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string StorageRoot { get; } = Environment.GetEnvironmentVariable("MASKFLOW_STORAGE") ?? Path.Combine(AppContext.BaseDirectory, "data", "storage");
    string StatePath => Environment.GetEnvironmentVariable("MASKFLOW_STATE") ?? Path.Combine(AppContext.BaseDirectory, "data", "maskflow-state.json");
    string MinioEndpoint => Environment.GetEnvironmentVariable("MASKFLOW_MINIO_ENDPOINT") ?? "http://192.168.3.43:9000";
    string MinioAccessKey => Environment.GetEnvironmentVariable("MASKFLOW_MINIO_ACCESS_KEY") ?? "minioadmin";
    string MinioSecretKey => Environment.GetEnvironmentVariable("MASKFLOW_MINIO_SECRET_KEY") ?? "minioadmin";
    string MinioBucket => Environment.GetEnvironmentVariable("MASKFLOW_MINIO_BUCKET") ?? "maskflow";
    bool UseMinio => !string.IsNullOrWhiteSpace(MinioEndpoint);
    public MaskFlowState State { get; private set; } = new();

    public MaskFlowStore(IMaskFlowRepository repository)
    {
        this.repository = repository;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        Directory.CreateDirectory(StorageRoot);
        repository.EnsureSchemaAsync().GetAwaiter().GetResult();
        var databaseState = repository.LoadAsync().GetAwaiter().GetResult();
        if (databaseState is not null)
        {
            State = databaseState;
        }
        else if (File.Exists(StatePath))
        {
            State = JsonSerializer.Deserialize<MaskFlowState>(File.ReadAllText(StatePath), json) ?? new MaskFlowState();
        }
        EnsureMinioBucketAsync().GetAwaiter().GetResult();
        Seed();
        SaveAsync().GetAwaiter().GetResult();
    }

    public async Task SaveAsync()
    {
        await gate.WaitAsync();
        try
        {
            await repository.SaveAsync(State);
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            await File.WriteAllTextAsync(StatePath, JsonSerializer.Serialize(State, json));
        }
        finally
        {
            gate.Release();
        }
    }

    AmazonS3Client CreateS3Client()
    {
        var config = new AmazonS3Config
        {
            ServiceURL = MinioEndpoint,
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1"
        };
        return new AmazonS3Client(new BasicAWSCredentials(MinioAccessKey, MinioSecretKey), config);
    }

    async Task EnsureMinioBucketAsync()
    {
        if (!UseMinio) return;
        using var client = CreateS3Client();
        var buckets = await client.ListBucketsAsync();
        if (buckets.Buckets.Any(x => x.BucketName == MinioBucket)) return;
        await client.PutBucketAsync(new PutBucketRequest { BucketName = MinioBucket });
    }

    public async Task<AuthResult?> RegisterAsync(string email, string password, string? username, HttpContext context)
    {
        email = email.Trim().ToLowerInvariant();
        if (State.Users.Any(x => x.Email == email)) return null;
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var user = new User(State.NextUserId++, email, HashPassword(password, salt), salt, "free", 10L * 1024 * 1024 * 1024, 0, DateTimeOffset.UtcNow, username, "", null);
        State.Users.Add(user);
        State.Quotas[user.Id.ToString()] = new AiQuota("free", ResolveDailyLimit("free"), 0, DateOnly.FromDateTime(DateTime.UtcNow));
        State.TeamMembers.Add(new TeamMember("mem_owner_" + user.Id, user.Id, email, "owner", "active", DateTimeOffset.UtcNow));
        State.Devices.Add(new AccountDevice("dev_" + Util.Id(), user.Id, "Current browser", context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null));
        var token = CreateSession(user.Id);
        await SaveAsync();
        return new AuthResult(token, PublicUser(user));
    }

    public async Task<AuthResult?> LoginAsync(string email, string password, HttpContext context)
    {
        var user = State.Users.FirstOrDefault(x => x.Email == email.Trim().ToLowerInvariant());
        if (user is null || !VerifyPassword(password, user.PasswordHash, user.Salt)) return null;
        State.Devices.Add(new AccountDevice("dev_" + Util.Id(), user.Id, "Current browser", context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null));
        var token = CreateSession(user.Id);
        await SaveAsync();
        return new AuthResult(token, PublicUser(user));
    }

    public User RequireUser(HttpContext context) => OptionalUser(context) ?? throw new BadHttpRequestException("Missing bearer token.", 401);
    public User? OptionalUser(HttpContext context)
    {
        var auth = context.Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = auth["Bearer ".Length..].Trim();
        var session = State.Sessions.FirstOrDefault(x => x.Token == token);
        if (session is null || session.ExpiresAt < DateTimeOffset.UtcNow) return null;
        return GetUser(session.UserId);
    }

    public User? GetUser(int id) => State.Users.FirstOrDefault(x => x.Id == id);
    public PublicUser PublicUser(User user) => new(user.Id, user.Email, user.Username ?? "MaskFlow User", user.Phone ?? "", user.AvatarPath is null ? null : "/api/account/avatar", user.Plan, user.QuotaBytes, user.UsedBytes, Math.Max(0, user.QuotaBytes - user.UsedBytes));

    public async Task UpdateProfileAsync(int userId, ProfileRequest request)
    {
        var user = GetUser(userId)!;
        State.Users.Remove(user);
        State.Users.Add(user with { Username = request.Username ?? user.Username, Phone = request.Phone ?? user.Phone });
        await SaveAsync();
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = GetUser(userId)!;
        if (!VerifyPassword(currentPassword, user.PasswordHash, user.Salt)) return false;
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        State.Users.Remove(user);
        State.Users.Add(user with { Salt = salt, PasswordHash = HashPassword(newPassword, salt) });
        await SaveAsync();
        return true;
    }

    static int ResolveDailyLimit(string plan)
    {
        var overrideLimit = Environment.GetEnvironmentVariable("MASKFLOW_AI_DAILY_LIMIT");
        if (!string.IsNullOrWhiteSpace(overrideLimit) && int.TryParse(overrideLimit, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return plan switch { "pro" => 1000, "team" => 100000, _ => 50 };
    }

    public void EnsureAiQuotaAvailable(User user, int amount = 1)
    {
        var quota = GetQuota(user);
        if (quota.DailyUsed + amount > quota.DailyLimit)
        {
            throw new BadHttpRequestException(
                $"Daily AI quota exceeded. Used {quota.DailyUsed}/{quota.DailyLimit} today (UTC).",
                429);
        }
    }

    public async Task UpdatePlanAsync(int userId, string plan)
    {
        var limit = ResolveDailyLimit(plan);
        var quota = plan switch { "pro" => 50L * 1024 * 1024 * 1024, "team" => 500L * 1024 * 1024 * 1024, _ => 10L * 1024 * 1024 * 1024 };
        var user = GetUser(userId)!;
        State.Users.Remove(user);
        State.Users.Add(user with { Plan = plan, QuotaBytes = quota });
        State.Quotas[userId.ToString()] = new AiQuota(plan, limit, 0, DateOnly.FromDateTime(DateTime.UtcNow));
        await SaveAsync();
    }

    public AiQuota GetQuota(User user)
    {
        var key = user.Id.ToString();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!State.Quotas.TryGetValue(key, out var quota) || quota.DailyResetAt < today)
        {
            var limit = ResolveDailyLimit(user.Plan);
            quota = new AiQuota(user.Plan, limit, 0, today);
            State.Quotas[key] = quota;
        }
        else if (quota.DailyLimit != ResolveDailyLimit(user.Plan))
        {
            quota = quota with { Plan = user.Plan, DailyLimit = ResolveDailyLimit(user.Plan) };
            State.Quotas[key] = quota;
        }
        return quota;
    }

    public async Task ConsumeAiQuotaAsync(int userId, int amount)
    {
        var user = GetUser(userId)!;
        var quota = GetQuota(user);
        if (quota.DailyUsed + amount > quota.DailyLimit) throw new BadHttpRequestException("Daily AI quota exceeded.", 429);
        State.Quotas[userId.ToString()] = quota with { DailyUsed = quota.DailyUsed + amount };
        await SaveAsync();
    }

    public async Task<FileItem> SaveUploadAsync(User user, IFormFile upload, string? projectId)
    {
        projectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId;
        if (projectId is not null && !State.Projects.Any(x => x.Id == projectId && x.UserId == user.Id))
        {
            throw new BadHttpRequestException("Project not found.", 404);
        }

        if (user.UsedBytes + upload.Length > user.QuotaBytes)
        {
            throw new BadHttpRequestException("Storage quota exceeded.", 403);
        }

        var safeName = Path.GetFileName(upload.FileName);
        var prefix = projectId is not null
            ? $"users/{user.Id}/projects/{projectId}/uploads"
            : $"users/{user.Id}/uploads";
        var objectKey = $"{prefix}/{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Util.Id()}_{safeName}";
        string path;
        if (UseMinio)
        {
            using var client = CreateS3Client();
            await using var stream = upload.OpenReadStream();
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = MinioBucket,
                Key = objectKey,
                InputStream = stream,
                ContentType = upload.ContentType ?? "application/octet-stream"
            });
            path = $"minio://{MinioBucket}/{objectKey}";
        }
        else
        {
            var userDir = projectId is not null
                ? Path.Combine(StorageRoot, user.Id.ToString(), "projects", projectId)
                : Path.Combine(StorageRoot, user.Id.ToString());
            Directory.CreateDirectory(userDir);
            path = Path.Combine(userDir, $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{safeName}");
            await using var target = File.Create(path);
            await upload.CopyToAsync(target);
        }
        var item = new FileItem(State.NextFileId++, user.Id, projectId, safeName, path, upload.Length, "image", upload.ContentType, DateTimeOffset.UtcNow, $"/api/files/{State.NextFileId - 1}/download");
        State.Files.Add(item);
        ReplaceUser(user with { UsedBytes = user.UsedBytes + upload.Length });
        return item;
    }

    public async Task<bool> DeleteProjectAsync(int userId, string projectId)
    {
        var project = State.Projects.FirstOrDefault(x => x.Id == projectId && x.UserId == userId);
        if (project is null) return false;

        var fileIds = State.Files.Where(x => x.UserId == userId && x.ProjectId == projectId).Select(x => x.Id).ToList();
        foreach (var fileId in fileIds)
        {
            await DeleteFileAsync(userId, fileId);
        }

        State.Tasks.RemoveAll(x => x.UserId == userId && x.ProjectId == projectId);
        State.Exports.RemoveAll(x => x.UserId == userId && x.ProjectId == projectId);
        State.ProjectLabels.Remove(projectId);
        State.Projects.Remove(project);
        await SaveAsync();
        return true;
    }

    public async Task<bool> DeleteFileAsync(int userId, int fileId)
    {
        var file = State.Files.FirstOrDefault(x => x.Id == fileId && x.UserId == userId);
        if (file is null) return false;
        State.Files.Remove(file);
        State.AnnotationSets.RemoveAll(x => x.FileId == fileId && x.UserId == userId);
        await DeleteStoredObjectAsync(file.Path);

        var user = GetUser(userId);
        if (user is not null)
        {
            ReplaceUser(user with { UsedBytes = Math.Max(0, user.UsedBytes - file.Size) });
        }

        await SaveAsync();
        return true;
    }

    public AnnotationSet SaveAnnotationSet(int userId, AnnotationSaveRequest request)
    {
        var file = State.Files.FirstOrDefault(x => x.Id == request.FileId && x.UserId == userId);
        if (file is null) throw new BadHttpRequestException("File not found.", 404);

        var projectLabels = file.ProjectId is not null ? GetProjectLabels(userId, file.ProjectId) : null;
        var annotations = NormalizeAnnotations(request.Annotations, projectLabels);
        var existing = State.AnnotationSets.FirstOrDefault(x => x.FileId == request.FileId && x.UserId == userId);
        if (existing is not null)
        {
            State.AnnotationSets.Remove(existing);
        }

        var now = DateTimeOffset.UtcNow;
        var set = new AnnotationSet(
            existing?.Id ?? "annset_" + Util.Id(),
            userId,
            request.FileId,
            request.Width,
            request.Height,
            annotations,
            BuildYoloTxt(annotations),
            existing?.CreatedAt ?? now,
            now);
        State.AnnotationSets.Add(set);
        return set;
    }

    public AnnotationSet? GetAnnotationSet(int userId, int fileId) => State.AnnotationSets.FirstOrDefault(x => x.UserId == userId && x.FileId == fileId);

    public List<string> GetProjectLabels(int userId, string projectId)
    {
        var project = State.Projects.FirstOrDefault(x => x.Id == projectId && x.UserId == userId);
        if (project is null) throw new BadHttpRequestException("Project not found.", 404);
        if (!State.ProjectLabels.TryGetValue(projectId, out var labels) || labels.Count == 0)
        {
            labels = ["object"];
            State.ProjectLabels[projectId] = labels;
        }
        if (!labels.Contains("object", StringComparer.OrdinalIgnoreCase)) labels.Insert(0, "object");
        return labels;
    }

    public List<string> SaveProjectLabels(int userId, string projectId, IEnumerable<string>? labels)
    {
        var project = State.Projects.FirstOrDefault(x => x.Id == projectId && x.UserId == userId);
        if (project is null) throw new BadHttpRequestException("Project not found.", 404);
        var cleaned = (labels ?? [])
            .Select(x => string.IsNullOrWhiteSpace(x) ? "" : x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!cleaned.Contains("object", StringComparer.OrdinalIgnoreCase)) cleaned.Insert(0, "object");
        State.ProjectLabels[projectId] = cleaned;
        return cleaned;
    }

    public static List<AnnotationItem> NormalizeAnnotations(List<AnnotationItem>? annotations, IReadOnlyList<string>? projectLabels = null)
    {
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (projectLabels is not null)
        {
            for (var i = 0; i < projectLabels.Count; i++)
            {
                if (!labels.ContainsKey(projectLabels[i])) labels[projectLabels[i]] = i;
            }
        }
        var normalized = new List<AnnotationItem>();
        foreach (var annotation in annotations ?? [])
        {
            var label = string.IsNullOrWhiteSpace(annotation.Label) ? "object" : annotation.Label.Trim();
            if (!labels.TryGetValue(label, out var classId))
            {
                classId = projectLabels is null ? labels.Count : Math.Max(0, annotation.ClassId);
                labels[label] = classId;
            }

            var box = annotation.Bbox;
            normalized.Add(annotation with
            {
                Id = string.IsNullOrWhiteSpace(annotation.Id) ? "ann_" + Util.Id() : annotation.Id,
                ClassId = classId,
                Label = label,
                Bbox = new YoloBox(Clamp01(box.Cx), Clamp01(box.Cy), Clamp01(box.Width), Clamp01(box.Height)),
                Segment = annotation.Segment?.Select(Clamp01).ToList(),
                Confidence = annotation.Confidence <= 0 ? 1.0 : annotation.Confidence
            });
        }

        return normalized;
    }

    public static string BuildYoloTxt(IEnumerable<AnnotationItem> annotations)
    {
        var lines = annotations.Select(annotation =>
        {
            if (annotation.Segment is { Count: >= 6 })
            {
                return $"{annotation.ClassId} {string.Join(" ", annotation.Segment.Select(FormatYolo))}";
            }

            var box = annotation.Bbox;
            return $"{annotation.ClassId} {FormatYolo(box.Cx)} {FormatYolo(box.Cy)} {FormatYolo(box.Width)} {FormatYolo(box.Height)}";
        });
        return string.Join("\n", lines);
    }

    public async Task<DatasetExport> CreateDatasetExportAsync(int userId, ExportRequest request)
    {
        var split = request.Split ?? new SplitConfig(70, 20, 10);
        if (split.Train + split.Val + split.Test != 100)
        {
            throw new BadHttpRequestException("Split ratios must sum to 100.", 400);
        }

        var exportId = "export_" + Util.Id();
        var exportDir = Path.Combine(StorageRoot, userId.ToString(), "exports");
        Directory.CreateDirectory(exportDir);
        var zipPath = Path.Combine(exportDir, $"{exportId}.zip");
        var files = State.Files
            .Where(x => x.UserId == userId && x.Kind == "image" && (request.ProjectId is null || x.ProjectId == request.ProjectId))
            .OrderBy(x => x.CreatedAt)
            .ToList();
        var fileIds = files.Select(x => x.Id).ToHashSet();
        var annotationMap = State.AnnotationSets.Where(x => x.UserId == userId && fileIds.Contains(x.FileId)).ToDictionary(x => x.FileId);
        var labeledFiles = files.Where(x => annotationMap.ContainsKey(x.Id)).ToList();
        if (labeledFiles.Count == 0)
        {
            throw new BadHttpRequestException("No annotated images found for export.", 400);
        }

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var labels = request.ProjectId is not null && State.ProjectLabels.TryGetValue(request.ProjectId, out var projectLabels)
                ? projectLabels.ToList()
                : annotationMap.Values
                .SelectMany(x => x.Annotations)
                .Select(x => x.Label)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
            if (labels.Count == 0) labels.Add("object");

            for (var index = 0; index < labeledFiles.Count; index++)
            {
                var file = labeledFiles[index];
                var targetSplit = SplitName(index, labeledFiles.Count, split);
                var extension = Path.GetExtension(file.Name);
                var stem = SanitizeFileName($"{file.Id}_{Path.GetFileNameWithoutExtension(file.Name)}");
                var imageEntry = archive.CreateEntry($"images/{targetSplit}/{stem}{extension}");
                await using (var imageEntryStream = imageEntry.Open())
                await using (var imageStream = await OpenStoredObjectAsync(file.Path))
                {
                    await imageStream.CopyToAsync(imageEntryStream);
                }

                var labelEntry = archive.CreateEntry($"labels/{targetSplit}/{stem}.txt");
                using var labelWriter = new StreamWriter(labelEntry.Open());
                labelWriter.Write(annotationMap[file.Id].YoloTxt);
            }

            var dataEntry = archive.CreateEntry("data.yaml");
            using (var writer = new StreamWriter(dataEntry.Open()))
            {
                writer.WriteLine("path: .");
                writer.WriteLine("train: images/train");
                writer.WriteLine("val: images/val");
                writer.WriteLine("test: images/test");
                writer.WriteLine($"nc: {labels.Count}");
                writer.WriteLine("names:");
                for (var i = 0; i < labels.Count; i++)
                {
                    writer.WriteLine($"  {i}: {labels[i]}");
                }
            }

            var readme = archive.CreateEntry("README.md");
            using var readmeWriter = new StreamWriter(readme.Open());
            readmeWriter.WriteLine("# MaskFlow YOLO Dataset");
            readmeWriter.WriteLine();
            readmeWriter.WriteLine($"Images: {labeledFiles.Count}");
            readmeWriter.WriteLine($"GeneratedAt: {DateTimeOffset.UtcNow:O}");
        }
        var zipSize = new FileInfo(zipPath).Length;
        var exportPath = zipPath;
        if (UseMinio)
        {
            var exportKey = $"users/{userId}/exports/{exportId}.zip";
            using (var client = CreateS3Client())
            await using (var zipStream = File.OpenRead(zipPath))
            {
                await client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = MinioBucket,
                    Key = exportKey,
                    InputStream = zipStream,
                    ContentType = "application/zip"
                });
            }
            exportPath = $"minio://{MinioBucket}/{exportKey}";
            File.Delete(zipPath);
        }
        var export = new DatasetExport(exportId, userId, request.ProjectId, request.TaskId, "completed", exportPath, zipSize, request, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, $"/api/export/{exportId}/download");
        State.Exports.Add(export);
        await SaveAsync();
        return export;
    }

    public async Task<Stream> OpenStoredObjectAsync(string path)
    {
        if (path.StartsWith("minio://", StringComparison.OrdinalIgnoreCase))
        {
            var (bucket, key) = ParseMinioPath(path);
            using var client = CreateS3Client();
            using var response = await client.GetObjectAsync(bucket, key);
            var memory = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memory);
            memory.Position = 0;
            return memory;
        }

        return File.OpenRead(path);
    }

    public async Task<bool> StoredObjectExistsAsync(string path)
    {
        if (path.StartsWith("minio://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var (bucket, key) = ParseMinioPath(path);
                using var client = CreateS3Client();
                await client.GetObjectMetadataAsync(bucket, key);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return File.Exists(path);
    }

    async Task DeleteStoredObjectAsync(string path)
    {
        if (path.StartsWith("minio://", StringComparison.OrdinalIgnoreCase))
        {
            var (bucket, key) = ParseMinioPath(path);
            using var client = CreateS3Client();
            await client.DeleteObjectAsync(bucket, key);
            return;
        }

        if (File.Exists(path)) File.Delete(path);
    }

    static (string Bucket, string Key) ParseMinioPath(string path)
    {
        var withoutScheme = path["minio://".Length..];
        var slash = withoutScheme.IndexOf('/');
        if (slash < 0) return (withoutScheme, "");
        return (withoutScheme[..slash], withoutScheme[(slash + 1)..]);
    }

    public Task<IResult> SetJobStatusAsync(string jobId, string status)
    {
        var job = State.Jobs.FirstOrDefault(x => x.Id == jobId);
        if (job is null) return Task.FromResult<IResult>(Results.NotFound(new { detail = "Job not found" }));
        State.Jobs.Remove(job);
        State.Jobs.Add(job with { Status = status, FinishedAt = status is "cancelled" or "succeeded" or "failed" ? DateTimeOffset.UtcNow : null });
        SaveAsync().GetAwaiter().GetResult();
        return Task.FromResult<IResult>(Results.Json(new { job = State.Jobs.First(x => x.Id == jobId) }));
    }

    public Task<object?> AddJobEventAsync(string jobId, JobEventCreate request)
    {
        var job = State.Jobs.FirstOrDefault(x => x.Id == jobId);
        if (job is null) return Task.FromResult<object?>(null);
        var ev = new JobEvent(State.NextEventId++, jobId, request.EventType, request.Payload, DateTimeOffset.UtcNow);
        State.JobEvents.Add(ev);
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            State.Jobs.Remove(job);
            State.Jobs.Add(job with { Status = request.Status, Error = request.Error ?? job.Error, FinishedAt = request.Status is "succeeded" or "failed" or "cancelled" ? DateTimeOffset.UtcNow : null });
        }
        SaveAsync().GetAwaiter().GetResult();
        return Task.FromResult<object?>(new { @event = ev, job = State.Jobs.First(x => x.Id == jobId) });
    }

    public Task<IResult> HeartbeatNodeAsync(string nodeId, NodeHeartbeat request)
    {
        var node = State.Nodes.FirstOrDefault(x => x.Id == nodeId);
        if (node is null) return Task.FromResult<IResult>(Results.NotFound(new { detail = "Node not found" }));
        State.Nodes.Remove(node);
        State.Nodes.Add(node with { Status = request.Status, GpuModel = request.GpuModel ?? node.GpuModel, VramGb = request.VramGb ?? node.VramGb, Region = request.Region ?? node.Region, LastHeartbeat = DateTimeOffset.UtcNow });
        SaveAsync().GetAwaiter().GetResult();
        return Task.FromResult<IResult>(Results.Json(new { node = State.Nodes.First(x => x.Id == nodeId).Public() }));
    }

    public Task<IResult> NodeStatusAsync(string nodeId, string status, bool approve = false)
    {
        var node = State.Nodes.FirstOrDefault(x => x.Id == nodeId);
        if (node is null) return Task.FromResult<IResult>(Results.NotFound(new { detail = "Node not found" }));
        State.Nodes.Remove(node);
        State.Nodes.Add(node with { Status = status, ApprovedAt = approve ? DateTimeOffset.UtcNow : node.ApprovedAt, LastHeartbeat = DateTimeOffset.UtcNow });
        SaveAsync().GetAwaiter().GetResult();
        return Task.FromResult<IResult>(Results.Json(new { node = State.Nodes.First(x => x.Id == nodeId).Public() }));
    }

    public Task<IResult> PollJobAsync(string nodeId)
    {
        var node = State.Nodes.FirstOrDefault(x => x.Id == nodeId);
        if (node is null) return Task.FromResult<IResult>(Results.NotFound(new { detail = "Node not found" }));
        var job = State.Jobs.Where(x => x.Status == "queued").OrderBy(x => x.CreatedAt).FirstOrDefault();
        if (job is null) return Task.FromResult<IResult>(Results.Json(new { job = (object?)null }));
        State.Jobs.Remove(job);
        var running = job with { Status = "running", NodeId = nodeId, StartedAt = DateTimeOffset.UtcNow };
        State.Jobs.Add(running);
        SaveAsync().GetAwaiter().GetResult();
        return Task.FromResult<IResult>(Results.Json(new { job = running }));
    }

    public TaskItem CreateTask(int userId, string type, string? title, string? projectId, int? fileId, int imageCount)
    {
        var job = CreateJob(type.Replace("auto_", "sam."), userId, projectId, new Dictionary<string, object?>(), new Dictionary<string, object?>());
        var task = new TaskItem("task_" + Util.Id(), userId, job.Id, type, title ?? type, projectId, fileId, imageCount, "queued", 0, null, null, DateTimeOffset.UtcNow, null, null);
        State.Tasks.Add(task);
        return task;
    }

    public TaskItem? UpdateTask(int userId, string taskId, string status, double progress, string? error)
    {
        var task = State.Tasks.FirstOrDefault(x => x.Id == taskId && x.UserId == userId);
        if (task is null) return null;
        State.Tasks.Remove(task);
        var fresh = task with { Status = status, Progress = progress, ErrorMessage = error, FinishedAt = status is "completed" or "failed" or "cancelled" ? DateTimeOffset.UtcNow : null };
        State.Tasks.Add(fresh);
        return fresh;
    }

    public Job CreateJob(string type, int userId, string? projectId, Dictionary<string, object?> input, Dictionary<string, object?> parameters)
    {
        var job = new Job("job_" + Util.Id(), "maskflow", type, userId, projectId, "platform", "free", "queued",
            new Dictionary<string, object?> { ["gpu"] = 1, ["timeoutSec"] = 600 }, input, null, parameters, null, 10, null, null, DateTimeOffset.UtcNow, null, null);
        State.Jobs.Add(job);
        return job;
    }

    string CreateSession(int userId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        State.Sessions.Add(new Session(token, userId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7)));
        return token;
    }

    void Seed()
    {
        if (State.Pools.Count == 0)
        {
            State.Pools.Add(new Pool("platform-gpu", "Platform GPU Pool", "platform-gpu", "cn-east", "active", new() { ["gpu"] = 8, ["vramGb"] = 192 }, new() { ["priority"] = "balanced" }, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            State.Pools.Add(new Pool("cpu-general", "General CPU Pool", "cpu-general", "cn-east", "active", new() { ["cpu"] = 64, ["memoryGb"] = 256 }, new() { ["priority"] = "low" }, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
        if (State.PricingRules.Count == 0)
        {
            State.PricingRules.Add(new PricingRule("price_gpu_default", "Default GPU Pricing", "gpu", "platform-gpu", "cn-east", 2.8, "hour", "active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
    }

    static string HashPassword(string password, string salt)
    {
        var bytes = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromHexString(salt), 150_000, HashAlgorithmName.SHA256, 32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    static bool VerifyPassword(string password, string hash, string salt) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashPassword(password, salt)), Encoding.ASCII.GetBytes(hash));

    void ReplaceUser(User user)
    {
        State.Users.RemoveAll(x => x.Id == user.Id);
        State.Users.Add(user);
    }

    static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));

    static string FormatYolo(double value) => Clamp01(value).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    static string SplitName(int index, int total, SplitConfig split)
    {
        if (total <= 0) return "train";
        var ratio = (index + 1) / (double)total * 100;
        if (ratio <= split.Train) return "train";
        if (ratio <= split.Train + split.Val) return "val";
        return "test";
    }
}