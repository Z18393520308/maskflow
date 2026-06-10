import { computed, nextTick, onMounted, provide, reactive, ref } from "vue";
import { apiFetch, authHeaders, clearSession, downloadAuthenticated, formatBytes, saveSession, session, user as currentUser } from "../lib/api";
import {
  buildAnnotationBoxStyle,
  buildAnnotationPolygonStyle,
  buildAnnotationRowAccentStyle,
  buildLabelChipStyle,
  buildLabelSwatchStyle,
  getLabelColor
} from "../lib/labelColors";
import {
  buildYoloTxt as formatYoloTxt,
  normalizeProjectDataType,
  projectDataTypeLabel,
  projectYoloExportHint
} from "../lib/yoloFormat";
import heroPreviewRoad from "../assets/hero-preview-road.png";

export function useMaskFlowApp() {
  const routes = {
    "/": "home",
    "/index.html": "home",
    "/auth.html": "auth",
    "/dashboard.html": "dashboard",
    "/segment.html": "segment",
    "/annotate.html": "annotate",
    "/export.html": "export",
    "/files.html": "files",
    "/records.html": "records",
    "/billing.html": "billing",
    "/settings.html": "settings"
  };

  const path = ref(window.location.pathname);
  const page = computed(() => routes[path.value] || "home");
  const account = ref(currentUser());
  const message = ref("");
  const loading = ref(false);

  const auth = reactive({ mode: new URLSearchParams(location.search).get("mode") || "login", email: "", username: "", password: "" });
  const dashboard = reactive({ projects: [], tasks: [], quota: null });
  const files = reactive({ rows: [], selected: null });
  const records = reactive({ rows: [] });
  const projects = reactive({ rows: [], selectedId: "", newName: "", newDataType: "detection", status: "" });
  const exportPage = reactive({
    tab: "config",
    split: { train: 70, val: 20, test: 10 },
    status: "",
    rows: []
  });
  const selectedProject = computed(() => projects.rows.find((item) => item.id === projects.selectedId) || null);
  const selectedProjectDataType = computed(() => normalizeProjectDataType(selectedProject.value?.dataType));
  const selectedProjectDataTypeLabel = computed(() => projectDataTypeLabel(selectedProject.value?.dataType));
  const selectedProjectExportHint = computed(() => projectYoloExportHint(selectedProject.value?.dataType));
  const segment = reactive({ file: null, preview: "", prompt: "", conf: 0.25, overlay: "", overlays: {}, activeOverlay: "all", categories: [], status: "准备就绪", warning: "", mode: "", count: 0 });
  const annotate = reactive({
    selected: null,
    fileId: null,
    current: null,
    preview: "",
    frame: { width: 0, height: 0 },
    annotations: [],
    activeId: "",
    width: 0,
    height: 0,
    conf: 0.25,
    labels: [],
    defaultRunLabel: "",
    newLabel: "",
    pendingDeleteLabel: "",
    labelDeleteReplace: "",
    status: "请选择或上传图片",
    dirty: false,
    savedAt: null,
    zoom: 1,
    filter: "all"
  });
  const settings = reactive({
    active: "profile",
    username: "",
    phone: "",
    currentPassword: "",
    newPassword: "",
    confirmPassword: "",
    notifications: { emailTask: true, emailBilling: true, browserNotice: true, weeklyReport: false },
    tokenName: "",
    tokenValue: "",
    tokens: [],
    teamEmail: "",
    teamRole: "member",
    members: [],
    devices: []
  });
  const settingsTabs = [
    ["profile", "个人信息"],
    ["password", "修改密码"],
    ["notifications", "通知设置"],
    ["tokens", "API Token"],
    ["team", "团队管理"],
    ["devices", "设备管理"]
  ];
  const billingPlans = [
    {
      id: "free",
      name: "Free",
      price: 0,
      storage: "10G",
      ai: "每日 50 次",
      audience: "个人体验",
      features: ["基础标注工具", "单图 AI 分割", "YOLO 数据集导出", "文件管理"]
    },
    {
      id: "pro",
      name: "Pro",
      price: 49,
      storage: "50G",
      ai: "每日 1000 次",
      audience: "专业用户",
      featured: true,
      features: ["批量自动标注", "高级标注工具", "API Token", "团队协作"]
    },
    {
      id: "team",
      name: "Team",
      price: 299,
      storage: "500G",
      ai: "高额额度",
      audience: "团队协作",
      features: ["高优先级处理", "团队成员管理", "专属技术支持", "自定义导出"]
    }
  ];
  const billingExplain = [
    ["存储", "存储空间", "用于存储上传图片、视频、标注数据和导出结果。"],
    ["AI", "AI 处理次数", "包括自动检测、智能分割、人工校正辅助等 AI 功能调用次数。"],
    ["标注", "标注工具", "不同套餐提供不同的标注工具和高级能力。"],
    ["团队", "团队协作", "支持团队成员管理、任务分配与协作标注。"]
  ];
  const billingFaqs = ["套餐可以随时升级或降级吗？", "AI 处理次数用完后怎么办？", "存储空间可以单独购买吗？", "如何开具发票？"];
  const homeFeatures = [
    ["AI", "AI 自动分割", "高精度分割，节省时间"],
    ["批量", "批量处理", "支持批量图片自动处理"],
    ["标注", "标注工具", "丰富的标注与编辑工具"],
    ["导出", "数据集导出", "支持 YOLO 格式导出"],
    ["云端", "云端加速", "按需算力，权限管理"],
    ["安全", "安全可靠", "隐私保护，权限管理"]
  ];

  function go(to) {
    history.pushState({}, "", to);
    path.value = window.location.pathname;
    message.value = "";
    if (needsLogin(page.value) && !account.value) {
      const redirect = encodeURIComponent(path.value + window.location.search);
      history.replaceState({}, "", `/auth.html?redirect=${redirect}`);
      path.value = "/auth.html";
      return;
    }
    refreshPage();
  }

  function needsLogin(name) {
    return !["home", "auth"].includes(name);
  }

  function logout() {
    clearSession();
    account.value = null;
    go("/index.html");
  }

  async function submitAuth() {
    loading.value = true;
    message.value = "";
    try {
      const endpoint = auth.mode === "register" ? "/api/auth/register" : "/api/auth/login";
      const data = await apiFetch(endpoint, {
        method: "POST",
        body: { email: auth.email, username: auth.username, password: auth.password }
      });
      saveSession(data);
      account.value = data.user;
      const redirect = new URLSearchParams(location.search).get("redirect");
      go(redirect && redirect.startsWith("/") ? redirect : "/dashboard.html");
    } catch (error) {
      message.value = error.message;
    } finally {
      loading.value = false;
    }
  }

  async function refreshDashboard() {
    if (!account.value) return;
    const [projects, tasks, quota] = await Promise.all([
      apiFetch("/api/projects").catch(() => ({ projects: [] })),
      apiFetch("/api/tasks").catch(() => ({ tasks: [] })),
      apiFetch("/api/ai/quota").catch(() => ({ quota: null }))
    ]);
    dashboard.projects = projects.projects || [];
    dashboard.tasks = tasks.tasks || [];
    dashboard.quota = quota.quota;
  }

  async function refreshProjects() {
    if (!account.value) return;
    const data = await apiFetch("/api/projects").catch(() => ({ projects: [] }));
    projects.rows = data.projects || [];
    dashboard.projects = projects.rows;
    if (!projects.selectedId && projects.rows.length) projects.selectedId = projects.rows[0].id;
    if (projects.selectedId && !projects.rows.some((project) => project.id === projects.selectedId)) {
      projects.selectedId = projects.rows[0]?.id || "";
    }
    await loadProjectLabels();
  }

  async function createProject() {
    const name = projects.newName.trim();
    if (!name) {
      projects.status = "请输入项目名称";
      return;
    }
    const data = await apiFetch("/api/projects", {
      method: "POST",
      body: {
        name,
        description: "",
        dataType: normalizeProjectDataType(projects.newDataType),
        split: { train: 70, val: 20, test: 10 }
      }
    });
    projects.newName = "";
    projects.selectedId = data.project.id;
    projects.status = `项目 ${data.project.name} 已创建`;
    await refreshProjects();
    await refreshFiles();
  }

  function syncExportSplitFromProject() {
    const split = selectedProject.value?.split;
    if (split) {
      exportPage.split.train = split.train;
      exportPage.split.val = split.val;
      exportPage.split.test = split.test;
    }
  }

  async function selectProject(projectId) {
    if (projectId !== projects.selectedId && !canLeaveCurrentAnnotation()) return;
    projects.selectedId = projectId;
    syncExportSplitFromProject();
    clearAnnotation();
    annotate.current = null;
    await loadProjectLabels();
    await refreshFiles();
    if (page.value === "export") await refreshExports();
  }

  async function deleteCurrentProject() {
    if (!projects.selectedId) return;
    const projectName = selectedProject.value?.name || "当前项目";
    await apiFetch(`/api/projects/${projects.selectedId}`, { method: "DELETE" });
    projects.status = `项目 ${projectName} 已删除`;
    if (annotate.preview) URL.revokeObjectURL(annotate.preview);
    annotate.preview = "";
    annotate.current = null;
    clearAnnotation();
    await refreshProjects();
    await refreshFiles();
  }

  async function loadProjectLabels() {
    if (!projects.selectedId) {
      annotate.labels = [];
      annotate.defaultRunLabel = "";
      return;
    }
    const data = await apiFetch(`/api/projects/${projects.selectedId}/labels`).catch(() => ({ labels: [] }));
    annotate.labels = data.labels ?? [];
    syncDefaultRunLabel();
  }

  function isUnassignedLabel(label) {
    return label === null || label === undefined || String(label).trim() === "";
  }

  function labelsEqual(a, b) {
    if (isUnassignedLabel(a) && isUnassignedLabel(b)) return true;
    if (isUnassignedLabel(a) || isUnassignedLabel(b)) return false;
    return String(a).trim().toLowerCase() === String(b).trim().toLowerCase();
  }

  function findLabelIndex(label) {
    if (isUnassignedLabel(label)) return -1;
    const normalized = String(label).trim().toLowerCase();
    return annotate.labels.findIndex((item) => item.toLowerCase() === normalized);
  }

  function syncDefaultRunLabel() {
    if (!annotate.labels.length) {
      annotate.defaultRunLabel = "";
      return;
    }
    const currentIndex = findLabelIndex(annotate.defaultRunLabel);
    if (currentIndex >= 0) return;
    annotate.defaultRunLabel = annotate.labels[0];
  }

  function isExportableAnnotation(item) {
    return !isUnassignedLabel(item?.label) && Number(item?.classId) >= 0;
  }

  function formatAnnotationLabel(label) {
    return isUnassignedLabel(label) ? "未分配" : label;
  }

  function isAnnotationConfirmed(item) {
    return Boolean(item?.confirmed ?? item?.Confirmed);
  }

  function normalizeAnnotationItem(item) {
    const normalized = { ...item };
    normalized.confirmed = isAnnotationConfirmed(item);
    return normalized;
  }

  async function refreshFiles() {
    if (!account.value) return;
    if (!projects.selectedId && ["files", "annotate", "export"].includes(page.value)) {
      files.rows = [];
      return;
    }
    const query = projects.selectedId ? `?projectId=${encodeURIComponent(projects.selectedId)}` : "";
    const data = await apiFetch(`/api/files${query}`).catch(() => ({ files: [] }));
    files.rows = data.files || [];
    if (data.user) {
      account.value = data.user;
      saveSession({ ...session(), user: data.user });
    }
  }

  async function refreshRecords() {
    if (!account.value) return;
    const data = await apiFetch("/api/tasks").catch(() => ({ tasks: [] }));
    records.rows = data.tasks || [];
  }

  const uploadQueue = reactive({
    active: false,
    total: 0,
    done: 0,
    failed: 0,
    skipped: 0,
    percent: 0,
    currentName: "",
    items: []
  });

  const uploadDuplicatePrompt = reactive({
    visible: false,
    duplicateNames: [],
    newCount: 0,
    resolve: null
  });

  function normalizeFileName(name) {
    return String(name || "").trim().toLowerCase();
  }

  function getDuplicateUploadNames(fileList) {
    const existing = new Set(files.rows.map((file) => normalizeFileName(file.name)));
    return [...new Set(Array.from(fileList).filter((file) => existing.has(normalizeFileName(file.name))).map((file) => file.name))];
  }

  function openDuplicateUploadPrompt(duplicateNames, newCount) {
    return new Promise((resolve) => {
      uploadDuplicatePrompt.visible = true;
      uploadDuplicatePrompt.duplicateNames = duplicateNames;
      uploadDuplicatePrompt.newCount = newCount;
      uploadDuplicatePrompt.resolve = resolve;
    });
  }

  function resolveDuplicateUpload(action) {
    const resolve = uploadDuplicatePrompt.resolve;
    uploadDuplicatePrompt.visible = false;
    uploadDuplicatePrompt.resolve = null;
    uploadDuplicatePrompt.duplicateNames = [];
    uploadDuplicatePrompt.newCount = 0;
    resolve?.(action);
  }

  async function beginSequentialUpload(fileList, projectId) {
    await refreshFiles();
    const items = Array.from(fileList);
    const duplicateNames = getDuplicateUploadNames(items);
    let skipNames = new Set();

    if (duplicateNames.length) {
      const duplicateSet = new Set(duplicateNames.map(normalizeFileName));
      const newCount = items.filter((file) => !duplicateSet.has(normalizeFileName(file.name))).length;
      const action = await openDuplicateUploadPrompt(duplicateNames, newCount);
      if (action === "cancel") return { cancelled: true, uploaded: [] };
      if (action === "skip") skipNames = duplicateSet;
    }

    const uploaded = await uploadFilesSequentially(items, projectId, { skipNames });
    return { cancelled: false, uploaded };
  }

  function resetUploadQueue(fileList) {
    const items = Array.from(fileList);
    uploadQueue.active = true;
    uploadQueue.total = items.length;
    uploadQueue.done = 0;
    uploadQueue.failed = 0;
    uploadQueue.skipped = 0;
    uploadQueue.percent = 0;
    uploadQueue.currentName = "";
    uploadQueue.items = items.map((file, index) => ({
      id: `${Date.now()}-${index}`,
      name: file.name,
      size: file.size,
      status: "pending",
      error: ""
    }));
  }

  function refreshUploadProgress() {
    uploadQueue.percent = uploadQueue.total
      ? Math.round(((uploadQueue.done + uploadQueue.failed + uploadQueue.skipped) / uploadQueue.total) * 100)
      : 0;
  }

  function uploadQueueStatusLabel(item) {
    if (item.status === "pending") return "等待";
    if (item.status === "uploading") return "上传中";
    if (item.status === "done") return "完成";
    if (item.status === "skipped") return "已跳过";
    return "失败";
  }

  function buildUploadSummary(uploaded, total) {
    const parts = [`成功 ${uploaded.length} 张`];
    if (uploadQueue.skipped) parts.push(`跳过 ${uploadQueue.skipped} 张`);
    if (uploadQueue.failed) parts.push(`失败 ${uploadQueue.failed} 张`);
    return `上传完成：${parts.join("，")}（共 ${total} 张）`;
  }

  async function uploadSingleFile(file, projectId) {
    const form = new FormData();
    form.append("projectId", projectId);
    form.append("files", file);
    return apiFetch("/api/files/upload", { method: "POST", body: form });
  }

  async function uploadFilesSequentially(fileList, projectId, options = {}) {
    const skipNames = options.skipNames || new Set();
    resetUploadQueue(fileList);
    const items = Array.from(fileList);
    const uploaded = [];
    for (let index = 0; index < items.length; index += 1) {
      const file = items[index];
      const queueItem = uploadQueue.items[index];
      if (skipNames.has(normalizeFileName(file.name))) {
        queueItem.status = "skipped";
        queueItem.error = "服务器已存在同名文件";
        uploadQueue.skipped += 1;
        refreshUploadProgress();
        continue;
      }
      queueItem.status = "uploading";
      uploadQueue.currentName = file.name;
      try {
        const data = await uploadSingleFile(file, projectId);
        if (data.user) {
          account.value = data.user;
          saveSession({ ...session(), user: data.user });
        }
        const saved = data.files?.[0];
        if (saved) uploaded.push(saved);
        queueItem.status = "done";
        uploadQueue.done += 1;
      } catch (error) {
        queueItem.status = "failed";
        queueItem.error = error.message;
        uploadQueue.failed += 1;
      }
      refreshUploadProgress();
    }
    uploadQueue.active = false;
    uploadQueue.currentName = "";
    return uploaded;
  }

  async function uploadFiles() {
    if (!files.selected?.length) return;
    if (!projects.selectedId) {
      projects.status = "请先选择或创建项目";
      return;
    }
    loading.value = true;
    projects.status = "正在上传图片";
    try {
      const total = files.selected.length;
      const result = await beginSequentialUpload(files.selected, projects.selectedId);
      if (result.cancelled) {
        projects.status = "已取消上传";
        return;
      }
      projects.status = uploadQueue.skipped || uploadQueue.failed
        ? buildUploadSummary(result.uploaded, total)
        : `已上传 ${result.uploaded.length} / ${total} 张图片`;
      await refreshFiles();
    } catch (error) {
      projects.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  async function deleteFile(fileId) {
    const data = await apiFetch(`/api/files/${fileId}`, { method: "DELETE" });
    if (data.user) {
      account.value = data.user;
      saveSession({ ...session(), user: data.user });
    }
    if (annotate.current?.id === fileId) {
      annotate.current = null;
      clearAnnotation();
    }
    await refreshFiles();
  }

  async function runSegment() {
    if (!segment.file) {
      segment.status = "请先选择一张图片";
      return;
    }
    loading.value = true;
    segment.status = "AI 正在分割";
    segment.overlay = "";
    segment.overlays = {};
    segment.activeOverlay = "all";
    segment.categories = [];
    segment.warning = "";
    segment.mode = "";
    segment.count = 0;
    try {
      const form = new FormData();
      form.append("image", segment.file);
      form.append("prompt", segment.prompt);
      form.append("conf", String(segment.conf));
      form.append("half", "true");
      const data = await apiFetch("/api/segment", { method: "POST", body: form });
      segment.overlays = data.overlays || {};
      segment.activeOverlay = "all";
      segment.overlay = data.overlay || segment.overlays.all || "";
      segment.categories = data.categories || [];
      segment.mode = data.mode || (segment.prompt ? "text" : "auto");
      segment.count = data.count || 0;
      segment.warning = data.warning || "";
      segment.status = segment.warning
        ? `已完成，识别出 ${segment.count} 个目标；自动分类失败，已返回分割结果`
        : `已完成，识别出 ${segment.count} 个目标`;
    } catch (error) {
      segment.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  function selectSegmentFile(file) {
    if (segment.preview) URL.revokeObjectURL(segment.preview);
    segment.file = file || null;
    segment.preview = file ? URL.createObjectURL(file) : "";
    segment.overlay = "";
    segment.overlays = {};
    segment.activeOverlay = "all";
    segment.categories = [];
    segment.warning = "";
    segment.mode = "";
    segment.count = 0;
    segment.status = file ? "图片已选择，可以开始分割" : "准备就绪";
  }

  function showSegmentOverlay(key) {
    if (!segment.overlays?.[key]) return;
    segment.activeOverlay = key;
    segment.overlay = segment.overlays[key];
  }

  function applyAnnotation(annotation, options = {}) {
    annotate.fileId = annotation.fileId;
    annotate.width = annotation.width || 0;
    annotate.height = annotation.height || 0;
    annotate.annotations = (annotation.annotations || []).map(normalizeAnnotationItem);
    annotate.activeId = annotate.annotations[0]?.id || "";
    normalizeAnnotationLabels();
    if (options.status) {
      markAnnotationSaved(options.status);
    } else if (annotate.annotations.length) {
      markAnnotationSaved(`已加载 ${annotate.annotations.length} 条标注`);
    } else {
      markAnnotationSaved("暂无标注");
    }
  }

  function clearAnnotation() {
    annotate.annotations = [];
    annotate.activeId = "";
    annotate.width = 0;
    annotate.height = 0;
    annotate.dirty = false;
  }

  async function persistBatchLabels() {
    if (!projects.selectedId) return annotate.labels;
    try {
      const data = await apiFetch(`/api/projects/${projects.selectedId}/labels`, {
        method: "PUT",
        body: { labels: annotate.labels }
      });
      annotate.labels = data.labels ?? annotate.labels;
      syncDefaultRunLabel();
      return annotate.labels;
    } catch (error) {
      annotate.status = error.message;
      throw error;
    }
  }

  function normalizeAnnotationLabels(items = annotate.annotations) {
    for (const item of items) {
      if (isUnassignedLabel(item.label)) {
        item.label = null;
        item.classId = -1;
        continue;
      }
      const classId = findLabelIndex(item.label);
      if (classId < 0) {
        item.label = null;
        item.classId = -1;
        continue;
      }
      item.label = annotate.labels[classId];
      item.classId = classId;
    }
    return items;
  }

  function syncAnnotationLabels() {
    normalizeAnnotationLabels();
    markAnnotationDirty("标签已更新，记得保存");
  }

  async function syncBatchLabelsFromAnnotations() {
    for (const item of annotate.annotations) {
      const label = isUnassignedLabel(item.label) ? "" : String(item.label).trim();
      if (label && findLabelIndex(label) < 0) annotate.labels.push(label);
    }
    await persistBatchLabels();
  }

  async function addAnnotateLabel() {
    const label = annotate.newLabel.trim();
    if (!label) return;
    if (findLabelIndex(label) < 0) annotate.labels.push(label);
    if (!annotate.labels.length || annotate.labels.length === 1) annotate.defaultRunLabel = label;
    await persistBatchLabels();
    annotate.newLabel = "";
  }

  function beginDeleteAnnotateLabel(label) {
    annotate.pendingDeleteLabel = label;
    const others = annotate.labels.filter((item) => item !== label);
    annotate.labelDeleteReplace = others[0] ?? "";
  }

  function cancelDeleteAnnotateLabel() {
    annotate.pendingDeleteLabel = "";
    annotate.labelDeleteReplace = "";
  }

  async function confirmDeleteAnnotateLabel() {
    const label = annotate.pendingDeleteLabel;
    if (!label || !projects.selectedId) return;
    const replaceWith = annotate.labelDeleteReplace;
    loading.value = true;
    annotate.status = `正在从项目中删除标签 ${label}`;
    try {
      const query = replaceWith ? `?replaceWith=${encodeURIComponent(replaceWith)}` : "";
      const data = await apiFetch(
        `/api/projects/${projects.selectedId}/labels/${encodeURIComponent(label)}${query}`,
        { method: "DELETE" }
      );
      annotate.labels = data.labels ?? [];
      if (annotate.defaultRunLabel === label) {
        annotate.defaultRunLabel = annotate.labels[0] ?? "";
      }
      syncDefaultRunLabel();
      if (annotate.current?.id) {
        try {
          const current = await apiFetch(`/api/annotations/file/${annotate.current.id}`);
          applyAnnotation(current.annotation);
        } catch {
          normalizeAnnotationLabels();
        }
      } else {
        normalizeAnnotationLabels();
      }
      await refreshFiles();
      annotate.pendingDeleteLabel = "";
      annotate.labelDeleteReplace = "";
      const tail = replaceWith ? `已替换为 ${replaceWith}` : "相关目标已标为未分配";
      annotate.status = `标签 ${label} 已删除，${tail}`;
    } catch (error) {
      annotate.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  function applyLabelToActive(label) {
    const active = annotate.annotations.find((item) => item.id === annotate.activeId);
    if (!active) {
      annotate.status = "请先在标注结果中选中一个目标";
      return;
    }
    active.label = label;
    normalizeAnnotationLabels();
    if (isExportableAnnotation(active)) active.confirmed = true;
    markAnnotationDirty(`已将选中目标设置为 ${label}`);
  }

  function applyAnnotationLabel(item, value) {
    item.label = value || null;
    normalizeAnnotationLabels([item]);
    if (isExportableAnnotation(item)) item.confirmed = true;
    markAnnotationDirty(isExportableAnnotation(item) ? "标签已更新并标记为人工已确认，记得保存" : "标签已更新，记得保存");
  }

  async function replaceLabelInBatch(fromLabel, toLabel) {
    const annotatedFiles = files.rows.filter((file) => file.annotated || file.id === annotate.current?.id);
    for (const file of annotatedFiles) {
      let annotation;
      if (file.id === annotate.current?.id) {
        annotation = {
          fileId: file.id,
          width: annotate.width,
          height: annotate.height,
          annotations: annotate.annotations.map((item) => ({ ...item }))
        };
      } else {
        try {
          const data = await apiFetch(`/api/annotations/file/${file.id}`);
          annotation = data.annotation;
        } catch {
          continue;
        }
      }
      const nextItems = (annotation.annotations || []).map((item) =>
        normalizeAnnotationItem({
          ...item,
          label: labelsEqual(item.label, fromLabel) ? toLabel : item.label
        })
      );
      normalizeAnnotationLabels(nextItems);
      const saved = await apiFetch(`/api/annotations/file/${file.id}`, {
        method: "PUT",
        body: { fileId: file.id, width: annotation.width, height: annotation.height, annotations: nextItems }
      });
      if (file.id === annotate.current?.id) applyAnnotation(saved.annotation);
    }
    await refreshFiles();
  }

  async function changeAnnotateFiles(fileList) {
    annotate.selected = fileList;
    await uploadAnnotateFiles();
  }

  async function uploadAnnotateFiles() {
    if (!annotate.selected?.length) return;
    if (!projects.selectedId) {
      annotate.status = "请先选择或创建项目";
      return;
    }
    loading.value = true;
    annotate.status = "正在上传图片";
    try {
      const total = annotate.selected.length;
      const result = await beginSequentialUpload(annotate.selected, projects.selectedId);
      if (result.cancelled) {
        annotate.status = "已取消上传";
        return;
      }
      await refreshFiles();
      const first = result.uploaded[0];
      if (first) await selectAnnotateFile(first);
      annotate.status = uploadQueue.skipped || uploadQueue.failed
        ? `${buildUploadSummary(result.uploaded, total)}，未自动标注`
        : `已上传 ${result.uploaded.length} / ${total} 张图片，未自动标注`;
    } catch (error) {
      annotate.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  async function loadAnnotatePreview(file) {
    if (annotate.preview) {
      URL.revokeObjectURL(annotate.preview);
      annotate.preview = "";
    }
    annotate.frame = { width: 0, height: 0 };
    if (!file?.downloadUrl) return;
    const response = await fetch(file.downloadUrl, { headers: authHeaders() });
    if (!response.ok) throw new Error("图片预览加载失败");
    annotate.preview = URL.createObjectURL(await response.blob());
    await nextTick();
  }

  const filteredFiles = computed(() => {
    if (annotate.filter === "annotated") return files.rows.filter((file) => file.annotated);
    if (annotate.filter === "unannotated") return files.rows.filter((file) => !file.annotated);
    return files.rows;
  });
  const currentFileIndex = computed(() => files.rows.findIndex((file) => file.id === annotate.current?.id));
  const activeAnnotation = computed(() => annotate.annotations.find((item) => item.id === annotate.activeId) || null);
  const canRunAnnotateAi = computed(() => annotate.labels.length > 0);
  const annotationStats = computed(() => {
    const total = annotate.annotations.length;
    const confirmed = annotate.annotations.filter((item) => isAnnotationConfirmed(item)).length;
    const unassigned = annotate.annotations.filter((item) => isUnassignedLabel(item.label)).length;
    return {
      total,
      confirmed,
      pending: Math.max(0, total - confirmed),
      unassigned
    };
  });
  const saveStateText = computed(() => {
    if (annotate.dirty) return "有未保存修改";
    if (annotate.savedAt) return `已保存 ${new Date(annotate.savedAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}`;
    return "等待标注";
  });

  function markAnnotationDirty(status = "有未保存修改") {
    annotate.dirty = true;
    annotate.status = status;
  }

  function markAnnotationSaved(status = "标注已保存") {
    annotate.dirty = false;
    annotate.savedAt = Date.now();
    annotate.status = status;
  }

  function canLeaveCurrentAnnotation() {
    return !annotate.dirty || window.confirm("当前图片有未保存修改，离开后将丢失这些改动。是否继续？");
  }

  async function selectAnnotateFile(file) {
    if (file?.id !== annotate.current?.id && !canLeaveCurrentAnnotation()) return;
    annotate.current = file;
    annotate.fileId = file?.id || null;
    clearAnnotation();
    annotate.dirty = false;
    annotate.savedAt = null;
    annotate.zoom = 1;
    if (!file) {
      annotate.status = "请选择或上传图片";
      return;
    }
    annotate.status = file.annotated
      ? "正在加载已保存标注"
      : "图片已选择，可单张分割标注或批量标注全部图片";
    try {
      await loadAnnotatePreview(file);
    } catch (error) {
      annotate.status = error.message;
    }
    if (file.annotated) {
      try {
        const data = await apiFetch(`/api/annotations/file/${file.id}`);
        applyAnnotation(data.annotation);
      } catch {
        clearAnnotation();
      }
    } else {
      markAnnotationSaved("图片已选择，等待自动标注");
    }
  }

  async function runMaskForFile(file, { updateCurrent = true } = {}) {
    const data = await apiFetch("/api/annotations/auto", {
      method: "POST",
      body: {
        fileId: file.id,
        conf: Number(annotate.conf),
        defaultLabel: annotate.defaultRunLabel || null
      }
    });
    if (updateCurrent) applyAnnotation(data.annotation);
    if (data.user) {
      account.value = data.user;
      saveSession({ ...session(), user: data.user });
    }
    return data.annotation;
  }

  async function runCurrentMask() {
    if (!annotate.current?.id) {
      annotate.status = "请先选择一张已上传图片";
      return;
    }
    if (!annotate.labels.length) {
      annotate.status = "请先添加至少一个项目标签";
      return;
    }
    loading.value = true;
    annotate.status = `正在分割标注：${annotate.current.name}`;
    try {
      await runMaskForFile(annotate.current, { updateCurrent: true });
      await refreshFiles();
      const refreshed = files.rows.find((file) => file.id === annotate.current.id) || annotate.current;
      await selectAnnotateFile(refreshed);
      annotate.status = `当前图片已生成 ${annotate.annotations.length} 条标注`;
    } catch (error) {
      annotate.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  async function runMasks() {
    if (!projects.selectedId) {
      annotate.status = "请先选择项目";
      return;
    }
    if (!files.rows.length) {
      annotate.status = "请先上传图片";
      return;
    }
    if (!annotate.labels.length) {
      annotate.status = "请先添加至少一个项目标签";
      return;
    }
    loading.value = true;
    const currentId = annotate.current?.id || files.rows[0]?.id;
    let success = 0;
    let failed = 0;
    try {
      for (const [index, file] of files.rows.entries()) {
        annotate.status = `AI 自动标注中：${index + 1} / ${files.rows.length} · ${file.name}`;
        try {
          await runMaskForFile(file, { updateCurrent: file.id === currentId });
          success += 1;
        } catch {
          failed += 1;
        }
      }
      await refreshFiles();
      const nextCurrent = files.rows.find((file) => file.id === currentId) || files.rows[0];
      if (nextCurrent) await selectAnnotateFile(nextCurrent);
      annotate.status = failed
        ? `已完成 ${success} 张，失败 ${failed} 张`
        : `已完成全部 ${success} 张图片的自动标注`;
    } catch (error) {
      annotate.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  async function saveAnnotation() {
    if (!annotate.current?.id) return;
    loading.value = true;
    const annotations = annotate.annotations.map((item) => {
      const normalized = normalizeAnnotationItem(item);
      if (isExportableAnnotation(normalized)) normalized.confirmed = true;
      return normalized;
    });
    normalizeAnnotationLabels(annotations);
    try {
      const data = await apiFetch(`/api/annotations/file/${annotate.current.id}`, {
        method: "PUT",
        body: {
          fileId: annotate.current.id,
          width: annotate.width,
          height: annotate.height,
          annotations
        }
      });
      applyAnnotation(data.annotation, { status: "标注已保存" });
      await refreshFiles();
    } catch (error) {
      annotate.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  function toggleAnnotationConfirmed(annotationId) {
    const item = annotate.annotations.find((a) => a.id === annotationId);
    if (item) {
      item.confirmed = !isAnnotationConfirmed(item);
      markAnnotationDirty(item.confirmed ? "目标已人工确认，记得保存" : "目标已取消确认，记得保存");
    }
  }

  function removeAnnotation(annotationId) {
    annotate.annotations = annotate.annotations.filter((item) => item.id !== annotationId);
    annotate.activeId = annotate.annotations[0]?.id || "";
    markAnnotationDirty("目标已删除，记得保存");
  }

  function yoloTxt() {
    normalizeAnnotationLabels();
    return formatYoloTxt(annotate.annotations, selectedProject.value?.dataType, isExportableAnnotation);
  }

  function downloadCurrentTxt() {
    const txt = yoloTxt();
    if (!txt) {
      annotate.status = "没有可导出的已分配标签标注";
      return;
    }
    const blob = new Blob([txt], { type: "text/plain" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `${annotate.current?.name?.replace(/\.[^.]+$/, "") || "annotation"}.txt`;
    a.click();
    URL.revokeObjectURL(url);
  }

  function annotationBoxStyle(item) {
    return buildAnnotationBoxStyle(item, annotate.labels, annotate.activeId === item.id);
  }

  function annotationPolygonStyle(item) {
    return buildAnnotationPolygonStyle(item, annotate.labels, annotate.activeId === item.id);
  }

  function labelChipStyle(label) {
    return buildLabelChipStyle(label, annotate.labels);
  }

  function labelSwatchStyle(label) {
    return buildLabelSwatchStyle(label, annotate.labels);
  }

  function annotationRowAccentStyle(item) {
    return buildAnnotationRowAccentStyle(item, annotate.labels);
  }

  function labelColor(label) {
    return getLabelColor(label, annotate.labels);
  }

  const yoloFrameStyle = computed(() => ({
    width: annotate.frame.width ? `${annotate.frame.width * annotate.zoom}px` : "auto",
    height: annotate.frame.height ? `${annotate.frame.height * annotate.zoom}px` : "auto"
  }));

  function updateYoloFrame(event) {
    const image = event?.target || document.querySelector(".annotate-canvas-panel .yolo-image-frame img");
    const canvas = image?.closest?.(".yolo-canvas");
    if (!image || !canvas || !image.naturalWidth || !image.naturalHeight) return;
    const canvasStyle = getComputedStyle(canvas);
    const paddingX = parseFloat(canvasStyle.paddingLeft) + parseFloat(canvasStyle.paddingRight);
    const paddingY = parseFloat(canvasStyle.paddingTop) + parseFloat(canvasStyle.paddingBottom);
    const viewportWidth = Math.max(1, canvas.clientWidth - paddingX);
    const viewportHeight = Math.max(1, canvas.clientHeight - paddingY);
    const imageRatio = image.naturalWidth / image.naturalHeight;
    const viewportRatio = viewportWidth / viewportHeight;
    let width;
    let height;
    if (viewportRatio > imageRatio) {
      height = viewportHeight;
      width = height * imageRatio;
    } else {
      width = viewportWidth;
      height = width / imageRatio;
    }
    annotate.frame = { width: Math.max(1, width), height: Math.max(1, height) };
  }

  function setYoloZoom(value) {
    annotate.zoom = Math.max(0.25, Math.min(3, Number(value) || 1));
    if (annotate.zoom === 1) nextTick(updateYoloFrame);
  }

  function resetYoloZoom() {
    annotate.zoom = 1;
    nextTick(updateYoloFrame);
  }

  async function selectAdjacentFile(direction) {
    if (!files.rows.length) return;
    const index = currentFileIndex.value;
    const nextIndex = Math.max(0, Math.min(files.rows.length - 1, (index < 0 ? 0 : index) + direction));
    const nextFile = files.rows[nextIndex];
    if (nextFile && nextFile.id !== annotate.current?.id) await selectAnnotateFile(nextFile);
  }

  function segmentPoints(item) {
    if (!item.segment?.length) return "";
    const points = [];
    for (let i = 0; i < item.segment.length - 1; i += 2) points.push(`${item.segment[i] * 100},${item.segment[i + 1] * 100}`);
    return points.join(" ");
  }

  function previewUrl(file) {
    return file?.id === annotate.current?.id ? annotate.preview : "";
  }

  function exportSplitTotal() {
    return Number(exportPage.split.train || 0) + Number(exportPage.split.val || 0) + Number(exportPage.split.test || 0);
  }

  function formatDateTime(value) {
    if (!value) return "-";
    return new Date(value).toLocaleString();
  }

  async function refreshExports() {
    if (!account.value) return;
    const query = projects.selectedId ? `?projectId=${encodeURIComponent(projects.selectedId)}` : "";
    const data = await apiFetch(`/api/export${query}`).catch(() => ({ exports: [] }));
    exportPage.rows = data.exports || [];
  }

  async function downloadExportItem(item) {
    if (!item?.downloadUrl) return;
    loading.value = true;
    exportPage.status = "正在下载导出文件...";
    try {
      await downloadAuthenticated(item.downloadUrl, `${item.id}.zip`);
      exportPage.status = "下载已开始";
    } catch (error) {
      exportPage.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  async function createExport() {
    if (!projects.selectedId) {
      exportPage.status = "请先选择项目";
      return;
    }
    if (exportSplitTotal() !== 100) {
      exportPage.status = "train / val / test 比例之和必须为 100";
      return;
    }
    if (!annotate.labels.length) {
      exportPage.status = "请先添加项目标签后再导出";
      if (page.value === "annotate") annotate.status = exportPage.status;
      return;
    }
    loading.value = true;
    exportPage.status = "正在生成数据集 ZIP，请稍候...";
    if (page.value === "annotate") annotate.status = exportPage.status;
    try {
      const data = await apiFetch("/api/export/dataset", {
        method: "POST",
        body: {
          projectId: projects.selectedId,
          format: "yolo",
          split: {
            train: Number(exportPage.split.train),
            val: Number(exportPage.split.val),
            test: Number(exportPage.split.test)
          }
        }
      });
      await downloadAuthenticated(data.export.downloadUrl, `${data.export.id}.zip`);
      exportPage.status = `导出成功，文件大小 ${formatBytes(data.export.size)}`;
      if (page.value === "annotate") annotate.status = exportPage.status;
      exportPage.tab = "history";
      await refreshExports();
    } catch (error) {
      const message = error.message.includes("No annotated images")
        ? "当前项目没有已标注图片，请先完成标注并保存后再导出"
        : error.message;
      exportPage.status = message;
      if (page.value === "annotate") annotate.status = message;
    } finally {
      loading.value = false;
    }
  }

  async function subscribe(plan) {
    loading.value = true;
    message.value = "";
    try {
      const data = await apiFetch("/api/billing/subscribe", { method: "POST", body: { plan } });
      account.value = data.user;
      saveSession({ ...session(), user: data.user });
      message.value = "套餐已更新";
    } catch (error) {
      message.value = error.message;
    } finally {
      loading.value = false;
    }
  }

  async function saveSettings() {
    const data = await apiFetch("/api/account/profile", { method: "PUT", body: { username: settings.username, phone: settings.phone } });
    account.value = data.user;
    saveSession({ ...session(), user: data.user });
    message.value = "账户信息已保存";
  }

  async function changePassword() {
    if (!settings.newPassword || settings.newPassword !== settings.confirmPassword) {
      message.value = "两次输入的新密码不一致";
      return;
    }
    await apiFetch("/api/account/password", {
      method: "POST",
      body: { currentPassword: settings.currentPassword, newPassword: settings.newPassword }
    });
    settings.currentPassword = "";
    settings.newPassword = "";
    settings.confirmPassword = "";
    message.value = "密码已更新";
  }

  async function loadAccountSettings() {
    if (!account.value) return;
    settings.username = account.value.username || "";
    settings.phone = account.value.phone || "";
    const [notifications, tokens, team, devices] = await Promise.all([
      apiFetch("/api/account/notifications").catch(() => ({ settings: settings.notifications })),
      apiFetch("/api/account/api-tokens").catch(() => ({ tokens: [] })),
      apiFetch("/api/account/team").catch(() => ({ members: [] })),
      apiFetch("/api/account/devices").catch(() => ({ devices: [] }))
    ]);
    settings.notifications = { ...settings.notifications, ...(notifications.settings || {}) };
    settings.tokens = tokens.tokens || [];
    settings.members = team.members || [];
    settings.devices = devices.devices || [];
  }

  async function saveNotifications() {
    const data = await apiFetch("/api/account/notifications", { method: "PUT", body: settings.notifications });
    settings.notifications = data.settings;
    message.value = "通知设置已保存";
  }

  async function createApiToken() {
    if (!settings.tokenName.trim()) {
      message.value = "请输入 Token 名称";
      return;
    }
    const data = await apiFetch("/api/account/api-tokens", { method: "POST", body: { name: settings.tokenName } });
    settings.tokenValue = data.value;
    settings.tokenName = "";
    await loadAccountSettings();
    message.value = "Token 已创建，请及时保存";
  }

  async function revokeApiToken(tokenId) {
    await apiFetch(`/api/account/api-tokens/${tokenId}`, { method: "DELETE" });
    await loadAccountSettings();
    message.value = "Token 已撤销";
  }

  async function addTeamMember() {
    if (!settings.teamEmail.trim()) {
      message.value = "请输入成员邮箱";
      return;
    }
    await apiFetch("/api/account/team", { method: "POST", body: { email: settings.teamEmail, role: settings.teamRole } });
    settings.teamEmail = "";
    settings.teamRole = "member";
    await loadAccountSettings();
    message.value = "成员邀请已创建";
  }

  async function removeTeamMember(memberId) {
    await apiFetch(`/api/account/team/${memberId}`, { method: "DELETE" });
    await loadAccountSettings();
    message.value = "成员已移除";
  }

  async function revokeDevice(deviceId) {
    await apiFetch(`/api/account/devices/${deviceId}/revoke`, { method: "POST" });
    await loadAccountSettings();
    message.value = "设备已撤销";
  }

  function refreshPage() {
    if (page.value === "dashboard") refreshDashboard();
    if (page.value === "billing") refreshDashboard();
    if (page.value === "files" || page.value === "annotate" || page.value === "export") {
      refreshProjects().then(refreshFiles);
    }
    if (page.value === "records") refreshRecords();
    if (page.value === "export") {
      syncExportSplitFromProject();
      refreshExports();
    }
    if (page.value === "settings" && account.value) loadAccountSettings();
  }


  onMounted(() => {
    window.addEventListener("popstate", () => {
      path.value = window.location.pathname;
      refreshPage();
    });
    window.addEventListener("resize", updateYoloFrame);
    if (needsLogin(page.value) && !session()) go("/auth.html");
    account.value = currentUser();
    refreshPage();
  });

  const providers = {
    page, auth, go, logout, homeFeatures, heroPreviewRoad, message, loading, uploadQueue, uploadQueueStatusLabel,
    submitAuth, annotate, projects, selectedProject, selectedProjectDataType, selectedProjectDataTypeLabel, selectedProjectExportHint, files, filteredFiles, account, formatBytes, dashboard, records,
    segment, settings, settingsTabs, billingPlans, billingExplain, billingFaqs, exportPage,
    saveSession, session, authHeaders, selectAnnotateFile, runCurrentMask, runMasks, saveAnnotation,
    removeAnnotation, toggleAnnotationConfirmed, activeAnnotation, annotationStats, currentFileIndex,
    saveStateText, yoloTxt, annotationBoxStyle, annotationPolygonStyle, labelChipStyle, labelSwatchStyle,
    annotationRowAccentStyle, labelColor, segmentPoints, yoloFrameStyle, updateYoloFrame,
    setYoloZoom, resetYoloZoom, selectAdjacentFile, addAnnotateLabel, beginDeleteAnnotateLabel,
    confirmDeleteAnnotateLabel, cancelDeleteAnnotateLabel, canRunAnnotateAi, formatAnnotationLabel,
    applyLabelToActive, applyAnnotationLabel, syncBatchLabelsFromAnnotations, syncAnnotationLabels, changeAnnotateFiles,
    previewUrl, downloadCurrentTxt, selectProject, createProject, deleteFile, deleteCurrentProject,
    createExport, exportSplitTotal, downloadExportItem, formatDateTime, needsLogin, uploadFiles,
    runSegment, selectSegmentFile, showSegmentOverlay, subscribe, saveSettings, changePassword,
    saveNotifications, createApiToken, revokeApiToken, addTeamMember, removeTeamMember, revokeDevice,
    refreshExports, uploadDuplicatePrompt, resolveDuplicateUpload
  };
  for (const [key, value] of Object.entries(providers)) {
    provide(key, value);
  }

  return {
    uploadDuplicatePrompt,
    resolveDuplicateUpload,
    page,
    path,
    auth,
    go,
    logout,
    account,
    message,
    loading,
    dashboard,
    files,
    records,
    projects,
    exportPage,
    selectedProject,
    selectedProjectDataType,
    selectedProjectDataTypeLabel,
    selectedProjectExportHint,
    segment,
    annotate,
    settings,
    settingsTabs,
    billingPlans,
    billingExplain,
    billingFaqs,
    homeFeatures,
    heroPreviewRoad,
    submitAuth,
    uploadQueue,
    uploadQueueStatusLabel,
    filteredFiles,
    currentFileIndex,
    activeAnnotation,
    annotationStats,
    saveStateText,
    yoloFrameStyle,
    needsLogin,
    refreshDashboard,
    createProject,
    selectProject,
    deleteCurrentProject,
    uploadFiles,
    deleteFile,
    runSegment,
    selectSegmentFile,
    showSegmentOverlay,
    selectAnnotateFile,
    runCurrentMask,
    runMasks,
    saveAnnotation,
    removeAnnotation,
    toggleAnnotationConfirmed,
    yoloTxt,
    downloadCurrentTxt,
    annotationBoxStyle,
    annotationPolygonStyle,
    labelChipStyle,
    labelSwatchStyle,
    annotationRowAccentStyle,
    labelColor,
    segmentPoints,
    updateYoloFrame,
    setYoloZoom,
    resetYoloZoom,
    selectAdjacentFile,
    addAnnotateLabel,
    beginDeleteAnnotateLabel,
    confirmDeleteAnnotateLabel,
    cancelDeleteAnnotateLabel,
    canRunAnnotateAi,
    formatAnnotationLabel,
    applyLabelToActive,
    applyAnnotationLabel,
    syncBatchLabelsFromAnnotations,
    syncAnnotationLabels,
    changeAnnotateFiles,
    previewUrl,
    createExport,
    exportSplitTotal,
    downloadExportItem,
    formatDateTime,
    subscribe,
    saveSettings,
    changePassword,
    saveNotifications,
    createApiToken,
    revokeApiToken,
    addTeamMember,
    removeTeamMember,
    revokeDevice,
    refreshExports
  };
}
