<script setup>
import { computed, nextTick, onMounted, provide, reactive, ref } from "vue";
import { apiFetch, authHeaders, clearSession, formatBytes, saveSession, session, user as currentUser } from "./lib/api";
import HomePage from "./components/pages/HomePage.vue";
import AuthPage from "./components/pages/AuthPage.vue";
import AnnotatePage from "./components/pages/AnnotatePage.vue";
import heroPreviewRoad from "./assets/hero-preview-road.png";

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
const projects = reactive({ rows: [], selectedId: "", newName: "", status: "" });
const selectedProject = computed(() => projects.rows.find((item) => item.id === projects.selectedId) || null);
const segment = reactive({ file: null, preview: "", prompt: "", conf: 0.25, overlay: "", overlays: {}, activeOverlay: "all", categories: [], status: "准备就绪", warning: "", mode: "", count: 0 });
const annotate = reactive({
  selected: null,
  fileId: null,
  current: null,
  tab: "workspace",
  preview: "",
  frame: { width: 0, height: 0 },
  annotations: [],
  activeId: "",
  width: 0,
  height: 0,
  conf: 0.25,
  labels: ["object"],
  newLabel: "",
  status: "请选择或上传图片"
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
  if (needsLogin(page.value) && !account.value) go("/auth.html");
  refreshPage();
}

function needsLogin(name) {
  return !["home", "auth", "segment"].includes(name);
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
    go("/dashboard.html");
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
    body: { name, description: "", dataType: "detection", split: { train: 70, val: 20, test: 10 } }
  });
  projects.newName = "";
  projects.selectedId = data.project.id;
  projects.status = `项目 ${data.project.name} 已创建`;
  await refreshProjects();
  await refreshFiles();
}

async function selectProject(projectId) {
  projects.selectedId = projectId;
  clearAnnotation();
  annotate.current = null;
  await loadProjectLabels();
  await refreshFiles();
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
    annotate.labels = ["object"];
    return;
  }
  const data = await apiFetch(`/api/projects/${projects.selectedId}/labels`).catch(() => ({ labels: ["object"] }));
  annotate.labels = data.labels?.length ? data.labels : ["object"];
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

async function uploadFiles() {
  if (!files.selected?.length) return;
  if (!projects.selectedId) {
    projects.status = "请先选择或创建项目";
    return;
  }
  const form = new FormData();
  form.append("projectId", projects.selectedId);
  Array.from(files.selected).forEach((file) => form.append("files", file));
  const data = await apiFetch("/api/files/upload", { method: "POST", body: form });
  if (data.user) {
    account.value = data.user;
    saveSession({ ...session(), user: data.user });
  }
  await refreshFiles();
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

function applyAnnotation(annotation) {
  annotate.fileId = annotation.fileId;
  annotate.width = annotation.width || 0;
  annotate.height = annotation.height || 0;
  annotate.annotations = (annotation.annotations || []).map((item) => ({ ...item }));
  annotate.activeId = annotate.annotations[0]?.id || "";
  normalizeAnnotationLabels();
  annotate.status = annotate.annotations.length ? `已加载 ${annotate.annotations.length} 条标注` : "暂无标注";
}

function clearAnnotation() {
  annotate.annotations = [];
  annotate.activeId = "";
  annotate.width = 0;
  annotate.height = 0;
}

function persistBatchLabels() {
  ensureDefaultLabel();
  if (!projects.selectedId) return;
  apiFetch(`/api/projects/${projects.selectedId}/labels`, {
    method: "PUT",
    body: { labels: annotate.labels }
  }).catch((error) => {
    annotate.status = error.message;
  });
}

function normalizeAnnotationLabels(items = annotate.annotations) {
  ensureDefaultLabel();
  for (const item of items) {
    if (!annotate.labels.includes(item.label)) item.label = "object";
    item.classId = Math.max(0, annotate.labels.indexOf(item.label));
  }
  return items;
}

function syncAnnotationLabels() {
  normalizeAnnotationLabels();
}

function syncBatchLabelsFromAnnotations() {
  for (const item of annotate.annotations) {
    const label = (item.label || "object").trim();
    if (label && !annotate.labels.includes(label)) annotate.labels.push(label);
  }
  persistBatchLabels();
}

function addAnnotateLabel() {
  const label = annotate.newLabel.trim();
  if (!label) return;
  if (!annotate.labels.includes(label)) annotate.labels.push(label);
  persistBatchLabels();
  annotate.newLabel = "";
}

function ensureDefaultLabel() {
  if (!annotate.labels.includes("object")) annotate.labels.unshift("object");
}

async function deleteAnnotateLabel(label) {
  if (label === "object") {
    annotate.status = "默认标签 object 不能删除";
    return;
  }
  annotate.labels = annotate.labels.filter((item) => item !== label);
  normalizeAnnotationLabels();
  ensureDefaultLabel();
  persistBatchLabels();
  annotate.status = `正在从本批次标注中移除 ${label}`;
  await replaceLabelInBatch(label, "object");
  annotate.status = `标签 ${label} 已删除，本批次中已使用该标签的目标已回退为 object`;
}

function applyLabelToActive(label) {
  const active = annotate.annotations.find((item) => item.id === annotate.activeId);
  if (!active) {
    annotate.status = "请先在标注结果中选中一个目标";
    return;
  }
  active.label = label;
  normalizeAnnotationLabels();
  annotate.status = `已将选中目标设置为 ${label}`;
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
    const nextItems = (annotation.annotations || []).map((item) => ({
      ...item,
      label: item.label === fromLabel ? toLabel : item.label
    }));
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
    const form = new FormData();
    form.append("projectId", projects.selectedId);
    Array.from(annotate.selected).forEach((file) => form.append("files", file));
    const data = await apiFetch("/api/files/upload", { method: "POST", body: form });
    if (data.user) {
      account.value = data.user;
      saveSession({ ...session(), user: data.user });
    }
    await refreshFiles();
    const first = data.files?.[0];
    if (first) await selectAnnotateFile(first);
    annotate.status = `已上传 ${data.files?.length || 0} 张图片，未自动标注`;
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

async function selectAnnotateFile(file) {
  annotate.current = file;
  annotate.fileId = file?.id || null;
  clearAnnotation();
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
  }
}

async function runMaskForFile(file, { updateCurrent = true } = {}) {
  const data = await apiFetch("/api/annotations/auto", {
    method: "POST",
    body: { fileId: file.id, conf: Number(annotate.conf) }
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
  normalizeAnnotationLabels();
  const data = await apiFetch(`/api/annotations/file/${annotate.current.id}`, {
    method: "PUT",
    body: {
      fileId: annotate.current.id,
      width: annotate.width,
      height: annotate.height,
      annotations: annotate.annotations
    }
  });
  applyAnnotation(data.annotation);
  await refreshFiles();
  annotate.status = "标注已保存";
}

function toggleAnnotationConfirmed(annotationId) {
  const item = annotate.annotations.find((a) => a.id === annotationId);
  if (item) item.confirmed = !item.confirmed;
}

async function removeAnnotation(annotationId) {
  annotate.annotations = annotate.annotations.filter((item) => item.id !== annotationId);
  annotate.activeId = annotate.annotations[0]?.id || "";
  await saveAnnotation();
}

function yoloTxt() {
  normalizeAnnotationLabels();
  return annotate.annotations.map((item) => {
    if (item.segment?.length >= 6) return `${item.classId || 0} ${item.segment.map((v) => Number(v).toFixed(6)).join(" ")}`;
    const box = item.bbox || { cx: 0.5, cy: 0.5, width: 0.2, height: 0.2 };
    return `${item.classId || 0} ${Number(box.cx).toFixed(6)} ${Number(box.cy).toFixed(6)} ${Number(box.width).toFixed(6)} ${Number(box.height).toFixed(6)}`;
  }).join("\n");
}

function downloadCurrentTxt() {
  const blob = new Blob([yoloTxt()], { type: "text/plain" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `${annotate.current?.name?.replace(/\.[^.]+$/, "") || "annotation"}.txt`;
  a.click();
  URL.revokeObjectURL(url);
}

function annotationBoxStyle(item) {
  const box = item.bbox || { cx: 0.5, cy: 0.5, width: 0.2, height: 0.2 };
  return {
    left: `${(box.cx - box.width / 2) * 100}%`,
    top: `${(box.cy - box.height / 2) * 100}%`,
    width: `${box.width * 100}%`,
    height: `${box.height * 100}%`
  };
}

const yoloFrameStyle = computed(() => ({
  width: annotate.frame.width ? `${annotate.frame.width}px` : "auto",
  height: annotate.frame.height ? `${annotate.frame.height}px` : "auto"
}));

function updateYoloFrame(event) {
  const image = event?.target || document.querySelector(".yolo-image-frame img");
  const canvas = image?.closest?.(".yolo-canvas");
  if (!image || !canvas || !image.naturalWidth || !image.naturalHeight) return;
  const canvasBox = canvas.getBoundingClientRect();
  const imageRatio = image.naturalWidth / image.naturalHeight;
  const canvasRatio = canvasBox.width / canvasBox.height;
  let width;
  let height;
  if (canvasRatio > imageRatio) {
    height = canvasBox.height;
    width = height * imageRatio;
  } else {
    width = canvasBox.width;
    height = width / imageRatio;
  }
  annotate.frame = { width: Math.max(1, width), height: Math.max(1, height) };
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

async function createExport() {
  if (!projects.selectedId) {
    message.value = "请先选择项目";
    return;
  }
  const data = await apiFetch("/api/export/dataset", {
    method: "POST",
    body: { projectId: projects.selectedId, format: "yolo", split: { train: 70, val: 20, test: 10 } }
  });
  location.href = data.export.downloadUrl;
}

async function subscribe(plan) {
  const data = await apiFetch("/api/billing/subscribe", { method: "POST", body: { plan } });
  account.value = data.user;
  saveSession({ ...session(), user: data.user });
  message.value = "套餐已更新";
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
  if (page.value === "settings" && account.value) loadAccountSettings();
}

provide("page", page);
provide("auth", auth);
provide("go", go);
provide("homeFeatures", homeFeatures);
provide("heroPreviewRoad", heroPreviewRoad);
provide("message", message);
provide("loading", loading);
provide("submitAuth", submitAuth);
provide("annotate", annotate);
provide("projects", projects);
provide("selectedProject", selectedProject);
provide("files", files);
provide("account", account);
provide("formatBytes", formatBytes);
provide("saveSession", saveSession);
provide("session", session);
provide("authHeaders", authHeaders);
provide("selectAnnotateFile", selectAnnotateFile);
provide("runCurrentMask", runCurrentMask);
provide("runMasks", runMasks);
provide("saveAnnotation", saveAnnotation);
provide("removeAnnotation", removeAnnotation);
provide("toggleAnnotationConfirmed", toggleAnnotationConfirmed);
provide("yoloTxt", yoloTxt);
provide("annotationBoxStyle", annotationBoxStyle);
provide("segmentPoints", segmentPoints);
provide("yoloFrameStyle", yoloFrameStyle);
provide("updateYoloFrame", updateYoloFrame);
provide("addAnnotateLabel", addAnnotateLabel);
provide("deleteAnnotateLabel", deleteAnnotateLabel);
provide("applyLabelToActive", applyLabelToActive);
provide("syncBatchLabelsFromAnnotations", syncBatchLabelsFromAnnotations);
provide("syncAnnotationLabels", syncAnnotationLabels);
provide("changeAnnotateFiles", changeAnnotateFiles);
provide("previewUrl", previewUrl);
provide("downloadCurrentTxt", downloadCurrentTxt);
provide("selectProject", selectProject);
provide("createProject", createProject);
provide("deleteFile", deleteFile);
provide("createExport", createExport);
provide("needsLogin", needsLogin);

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
</script>

<template>
  <header v-if="page === 'home' || page === 'auth'" class="marketing-nav">
    <a class="marketing-brand" href="#" @click.prevent="go('/index.html')"><span class="logo-mark">M</span><strong>MaskFlow</strong></a>
    <nav class="marketing-links">
      <a href="#features">功能</a>
      <a href="#" @click.prevent="go('/billing.html')">价格</a>
      <a href="#" @click.prevent="go('/records.html')">帮助文档</a>
      <a href="#features">博客</a>
    </nav>
    <div class="marketing-actions">
      <a class="btn secondary" href="#" @click.prevent="go('/auth.html')">登录</a>
      <a class="btn" href="#" @click.prevent="auth.mode = 'register'; go('/auth.html')">注册</a>
    </div>
  </header>

  <template v-else>
    <header class="mf-topbar">
      <a class="marketing-brand" href="#" @click.prevent="go('/dashboard.html')"><span class="logo-mark">M</span><strong>MaskFlow</strong></a>
      <nav class="mf-nav">
        <a :class="{ active: page === 'dashboard' }" href="#" @click.prevent="go('/dashboard.html')">控制台</a>
        <a :class="{ active: page === 'annotate' }" href="#" @click.prevent="go('/annotate.html')">AI 标注工具</a>
        <a :class="{ active: page === 'export' }" href="#" @click.prevent="go('/export.html')">数据集</a>
        <a :class="{ active: page === 'billing' }" href="#" @click.prevent="go('/billing.html')">账单套餐</a>
        <a :class="{ active: page === 'files' }" href="#" @click.prevent="go('/files.html')">图片管理</a>
      </nav>
      <div class="mf-userbar">
        <button class="icon-btn">?</button>
        <span class="user-mini"><span class="avatar">M</span>{{ account?.username || "MaskFlow User" }}</span>
        <button class="btn secondary" @click="logout">退出</button>
      </div>
    </header>
  </template>

  <aside v-if="!['home', 'auth'].includes(page)" class="app-sidebar">
    <a class="app-sidebar-brand" href="#" @click.prevent="go('/dashboard.html')"><span class="logo-mark">M</span><strong>MaskFlow</strong></a>
    <nav>
      <a :class="{ active: page === 'dashboard' }" href="#" @click.prevent="go('/dashboard.html')"><span>📊</span>控制台</a>
      <a :class="{ active: page === 'files' }" href="#" @click.prevent="go('/files.html')"><span>📤</span>上传图片</a>
      <a :class="{ active: page === 'segment' }" href="#" @click.prevent="go('/segment.html')"><span>✂️</span>SAM 分割</a>
      <a :class="{ active: page === 'annotate' }" href="#" @click.prevent="go('/annotate.html')"><span>🏷️</span>YOLO 标注</a>
      <a :class="{ active: page === 'export' }" href="#" @click.prevent="go('/export.html')"><span>📦</span>数据集导出</a>
      <a :class="{ active: page === 'records' }" href="#" @click.prevent="go('/records.html')"><span>📋</span>处理记录</a>
      <a :class="{ active: page === 'billing' }" href="#" @click.prevent="go('/billing.html')"><span>💳</span>账单套餐</a>
      <a :class="{ active: page === 'settings' }" href="#" @click.prevent="go('/settings.html')"><span>⚙️</span>账户设置</a>
    </nav>
    <div class="app-sidebar-account">
      <span class="avatar">M</span>
      <div><strong>{{ account?.username || "MaskFlow User" }}</strong><small>{{ account?.plan || "Free" }}</small></div>
      <button @click="logout">退出</button>
    </div>
  </aside>

  <HomePage />
  <AuthPage />

  <main v-if="page === 'dashboard'" class="mf-main page-pad work-page">
    <section class="work-wrap">
      <header class="work-title">
        <div class="work-title-icon">D</div>
        <div><h1>控制台</h1><p>查看项目、图片、任务和 AI 配额的整体运行状态。</p></div>
      </header>
      <nav class="billing-proto-tabs work-tabs"><a class="active">概览</a><a>最近任务</a></nav>
      <section class="work-metrics">
        <article><span>当前项目</span><strong>{{ dashboard.projects.length }}</strong><p>正在维护的数据标注项目</p></article>
        <article><span>上传图片</span><strong>{{ files.rows.length }}</strong><p>当前工作区图片资源</p></article>
        <article><span>处理任务</span><strong>{{ dashboard.tasks.length }}</strong><p>SAM、YOLO 与导出任务</p></article>
        <article><span>今日 AI 次数</span><strong>{{ dashboard.quota ? (dashboard.quota.dailyLimit - dashboard.quota.dailyUsed) + '/' + dashboard.quota.dailyLimit : '50/50' }}</strong><p>每日自动重置</p></article>
      </section>
      <section class="work-bottom">
        <article class="work-card wide">
          <h2>最近处理记录</h2>
          <table><tbody><tr v-for="task in dashboard.tasks.slice(0,5)" :key="task.id"><td>{{ task.title }}</td><td>{{ task.type }}</td><td>{{ task.status }}</td><td>{{ task.createdAt }}</td></tr><tr v-if="!dashboard.tasks.length"><td colspan="4">暂无处理记录。</td></tr></tbody></table>
        </article>
        <article class="work-card"><h2>快捷入口</h2><button class="btn" @click="go('/files.html')">上传图片</button><button class="btn secondary" @click="go('/segment.html')">SAM 分割</button><button class="btn secondary" @click="go('/annotate.html')">YOLO 标注</button></article>
      </section>
    </section>
  </main>

  <main v-if="page === 'segment'" class="mf-main page-pad work-page">
    <section class="work-wrap">
      <header class="work-title">
        <div class="work-title-icon">S</div>
        <div><h1>SAM 分割</h1><p>上传图片并使用提示词与置信度参数生成分割结果。</p></div>
      </header>
      <nav class="billing-proto-tabs work-tabs"><a class="active">单图分割</a><a>结果列表</a></nav>
      <section class="work-tool-grid segment-tool-grid">
        <aside class="work-card">
          <h2>分割配置</h2>
          <input type="file" accept="image/*" @change="selectSegmentFile($event.target.files[0])" />
          <label>提示词<input v-model="segment.prompt" placeholder="留空自动识别，或输入 person, car, box" /></label>
          <label>置信度 {{ segment.conf }}<input v-model="segment.conf" type="range" min="0.01" max="0.95" step="0.01" /></label>
          <button class="btn" :disabled="loading || !segment.file" @click="runSegment">{{ loading ? '分割中...' : '开始分割' }}</button>
          <p>{{ segment.status }}</p>
        </aside>
        <section class="work-stage segment-stage">
          <img v-if="segment.overlay" :src="segment.overlay" alt="分割结果" />
          <img v-else-if="segment.preview" :src="segment.preview" alt="待分割图片" />
          <div v-else>选择图片后开始 AI 分割</div>
        </section>
        <aside class="work-card segment-result-card">
          <h2>分割结果</h2>
          <p v-if="segment.mode">模式：{{ segment.mode === 'text' ? '文本提示' : '自动识别' }}</p>
          <p v-if="segment.count">目标数量：{{ segment.count }}</p>
          <p v-if="segment.warning" class="segment-warning">自动分类暂不可用，已先返回分割结果。可输入 person, car 等提示词获得类别结果。</p>
          <div class="overlay-list">
            <button v-if="segment.overlays.all" :class="['overlay-row', { active: segment.activeOverlay === 'all' }]" type="button" @click="showSegmentOverlay('all')"><span>全部目标</span><b>{{ segment.count }}</b></button>
            <button v-for="cat in segment.categories" :key="cat.id" :class="['overlay-row', { active: segment.activeOverlay === cat.id }]" type="button" @click="showSegmentOverlay(cat.id)"><span>{{ cat.label }}</span><b>{{ cat.count }}</b></button>
          </div>
          <p v-if="!segment.categories.length">运行后将在这里显示识别类别。</p>
        </aside>
      </section>
    </section>
  </main>

  <AnnotatePage v-if="page === 'annotate'" />

  <main v-if="page === 'files'" class="mf-main page-pad work-page">
    <section class="work-wrap">
      <header class="work-title">
        <div class="work-title-icon">U</div>
        <div><h1>上传图片</h1><p>按项目管理用于 SAM 分割、YOLO 标注和数据集导出的图片素材。</p></div>
      </header>
      <nav class="billing-proto-tabs work-tabs"><a class="active">图片文件</a><a>上传记录</a></nav>
      <section class="project-bar">
        <div><strong>当前项目</strong><p>{{ selectedProject?.name || '请先选择或创建项目' }}</p></div>
        <select v-model="projects.selectedId" @change="selectProject(projects.selectedId)">
          <option value="">选择项目</option>
          <option v-for="project in projects.rows" :key="project.id" :value="project.id">{{ project.name }} · {{ project.imageCount || 0 }} 张</option>
        </select>
        <input v-model="projects.newName" placeholder="新项目名称，例如 药瓶分类" @keyup.enter="createProject" />
        <button class="btn compact-btn" type="button" @click="createProject">新增项目</button>
        <button class="btn secondary compact-btn" type="button" :disabled="!projects.selectedId" @click="deleteCurrentProject">删除项目</button>
      </section>
      <section class="work-bottom">
        <article class="work-card">
          <h2>上传图片</h2>
          <input type="file" multiple accept="image/*" @change="files.selected = $event.target.files" />
          <p>{{ files.selected?.length ? '已选择 ' + files.selected.length + ' 个文件' : '支持批量选择图片文件。' }}</p>
          <button class="btn" :disabled="loading || !files.selected?.length || !projects.selectedId" @click="uploadFiles">上传到当前项目</button>
          <p v-if="projects.status">{{ projects.status }}</p>
          <p v-if="account">空间：{{ formatBytes(account.usedBytes) }} / {{ formatBytes(account.quotaBytes) }}</p>
        </article>
        <article class="work-card wide">
          <h2>文件列表</h2>
          <table>
            <tbody>
              <tr v-for="file in files.rows" :key="file.id">
                <td>{{ file.name }}</td>
                <td>{{ formatBytes(file.size) }}</td>
                <td>{{ file.annotationCount || 0 }} 条标注</td>
                <td>{{ file.createdAt }}</td>
                <td><a :href="file.downloadUrl">下载</a> <button class="text-danger" type="button" @click="deleteFile(file.id)">删除</button></td>
              </tr>
              <tr v-if="!files.rows.length"><td colspan="5">当前项目暂无图片文件。</td></tr>
            </tbody>
          </table>
        </article>
      </section>
    </section>
  </main>

  <main v-if="page === 'records'" class="mf-main page-pad work-page">
    <section class="work-wrap">
      <header class="work-title">
        <div class="work-title-icon">R</div>
        <div><h1>处理记录</h1><p>追踪自动分割、标注、导出等任务的执行状态。</p></div>
      </header>
      <nav class="billing-proto-tabs work-tabs"><a class="active">全部</a><a>运行中</a><a>已完成</a><a>失败</a><a>已取消</a></nav>
      <section class="work-card">
        <h2>任务列表</h2>
        <table>
          <tbody>
            <tr v-for="task in records.rows" :key="task.id">
              <td>{{ task.title }}</td>
              <td>{{ task.type }}</td>
              <td>{{ task.status }}</td>
              <td>{{ task.imageCount }}</td>
              <td>{{ task.createdAt }}</td>
            </tr>
            <tr v-if="!records.rows.length"><td colspan="5">暂无处理任务。</td></tr>
          </tbody>
        </table>
      </section>
    </section>
  </main>

  <main v-if="page === 'export'" class="mf-main page-pad work-page">
    <section class="work-wrap">
      <header class="work-title">
        <div class="work-title-icon">E</div>
        <div><h1>数据集导出</h1><p>按 YOLO 目录结构生成训练集、验证集和配置文件。</p></div>
      </header>
      <nav class="billing-proto-tabs work-tabs"><a class="active">导出配置</a><a>历史记录</a></nav>
      <section class="project-bar">
        <div><strong>导出项目</strong><p>{{ selectedProject?.name || '请先选择项目' }}</p></div>
        <select v-model="projects.selectedId" @change="selectProject(projects.selectedId)">
          <option value="">选择项目</option>
          <option v-for="project in projects.rows" :key="project.id" :value="project.id">{{ project.name }} · {{ project.imageCount || 0 }} 张 · {{ project.annotationCount || 0 }} 条标注</option>
        </select>
      </section>
      <section class="work-bottom">
        <article class="work-card">
          <h2>导出配置</h2>
          <p>项目：{{ selectedProject?.name || '-' }}</p>
          <p>train 70% / val 20% / test 10%</p>
          <p>格式：YOLO txt</p>
          <button class="btn" :disabled="!projects.selectedId" @click="createExport">导出当前项目 ZIP</button>
        </article>
        <article class="work-card wide">
          <h2>目录预览</h2>
          <pre>{{ selectedProject?.name || 'project' }}/
  images/train
  images/val
  images/test
  labels/train
  labels/val
  labels/test
  data.yaml</pre>
        </article>
      </section>
    </section>
  </main>

  <main v-if="page === 'billing'" class="mf-main page-pad billing-proto-page">
    <section class="billing-proto-wrap">
      <header class="billing-proto-title">
        <div class="billing-title-icon">B</div>
        <div><h1>账单套餐</h1><p>选择适合当前标注规模的 AI 处理次数和存储空间。</p></div>
      </header>
      <nav class="billing-proto-tabs"><a class="active">套餐订阅</a><a>使用记录</a></nav>
      <section class="billing-proto-plans">
        <article v-for="plan in billingPlans" :key="plan.id" :class="['billing-proto-card', { featured: plan.id === 'pro' }]">
          <b v-if="plan.id === 'pro'" class="billing-recommend">推荐</b>
          <h2>{{ plan.name }}</h2>
          <div class="billing-proto-price"><strong>¥{{ plan.price }}</strong><span>/月</span></div>
          <ul><li v-for="feature in plan.features" :key="feature"><span>✓</span>{{ feature }}</li></ul>
          <button :class="{ primary: plan.id === 'pro' }" :disabled="account?.plan === plan.id" @click="subscribe(plan.id)">{{ account?.plan === plan.id ? '当前套餐' : '立即升级' }}</button>
        </article>
      </section>
      <section class="billing-proto-bottom">
        <article class="billing-explain-card"><h2>套餐说明</h2><div class="billing-explain-grid"><div v-for="item in billingExplain" :key="item[1]"><span>{{ item[0] }}</span><section><h3>{{ item[1] }}</h3><p>{{ item[2] }}</p></section></div></div></article>
        <article class="billing-faq-card"><h2>常见问题</h2><p v-for="faq in billingFaqs" :key="faq"><span>{{ faq }}</span><b>›</b></p></article>
      </section>
      <footer class="billing-proto-note">所有套餐按月计费，可随时取消。升级后剩余资源将自动叠加。</footer>
    </section>
  </main>

  <main v-if="page === 'settings'" class="mf-main page-pad account-settings-page">
    <div class="settings-heading"><div><p class="page-kicker">Account</p><h1>账户设置</h1></div><p v-if="message" class="settings-toast">{{ message }}</p></div>
    <section class="account-settings-layout">
      <nav class="account-settings-nav"><button v-for="tab in settingsTabs" :key="tab[0]" :class="{ active: settings.active === tab[0] }" type="button" @click="settings.active = tab[0]; message = ''">{{ tab[1] }}</button></nav>
      <section v-if="settings.active === 'profile'" class="account-settings-card">
        <div class="settings-card-title"><h2>个人信息</h2><p>维护账户展示信息和联系方式。</p></div>
        <form class="settings-form-grid" @submit.prevent="saveSettings">
          <label><span>用户名</span><input v-model="settings.username" placeholder="请输入用户名" /></label>
          <label><span>邮箱</span><input :value="account?.email" disabled /></label>
          <label><span>手机号</span><input v-model="settings.phone" placeholder="请输入手机号" /></label>
          <div class="settings-actions"><button class="btn" type="submit">保存修改</button></div>
        </form>
      </section>
      <section v-if="settings.active === 'password'" class="account-settings-card">
        <div class="settings-card-title"><h2>修改密码</h2><p>建议使用至少 8 位，包含字母和数字的密码。</p></div>
        <form class="settings-form-grid" @submit.prevent="changePassword">
          <label><span>当前密码</span><input v-model="settings.currentPassword" type="password" autocomplete="current-password" /></label>
          <label><span>新密码</span><input v-model="settings.newPassword" type="password" autocomplete="new-password" /></label>
          <label><span>确认新密码</span><input v-model="settings.confirmPassword" type="password" autocomplete="new-password" /></label>
          <div class="settings-actions"><button class="btn" type="submit">更新密码</button></div>
        </form>
      </section>
      <section v-if="settings.active === 'notifications'" class="account-settings-card">
        <div class="settings-card-title"><h2>通知设置</h2><p>选择希望接收的任务、账单和报告通知。</p></div>
        <div class="settings-switch-list">
          <label><input v-model="settings.notifications.emailTask" type="checkbox" /> <span>任务完成邮件通知</span></label>
          <label><input v-model="settings.notifications.emailBilling" type="checkbox" /> <span>账单和套餐邮件通知</span></label>
          <label><input v-model="settings.notifications.browserNotice" type="checkbox" /> <span>浏览器内通知提醒</span></label>
          <label><input v-model="settings.notifications.weeklyReport" type="checkbox" /> <span>每周处理报告</span></label>
        </div>
        <div class="settings-actions"><button class="btn" type="button" @click="saveNotifications">保存通知设置</button></div>
      </section>
      <section v-if="settings.active === 'tokens'" class="account-settings-card">
        <div class="settings-card-title"><h2>API Token</h2><p>用于通过接口上传图片、创建任务和导出数据集。</p></div>
        <div class="settings-inline-form">
          <input v-model="settings.tokenName" placeholder="Token 名称，例如本地脚本" />
          <button class="btn" type="button" @click="createApiToken">创建 Token</button>
        </div>
        <div v-if="settings.tokenValue" class="token-secret"><span>新 Token</span><code>{{ settings.tokenValue }}</code></div>
        <table class="settings-table">
          <thead><tr><th>名称</th><th>前缀</th><th>创建时间</th><th>最后使用</th><th></th></tr></thead>
          <tbody>
            <tr v-for="token in settings.tokens" :key="token.id">
              <td>{{ token.name }}</td>
              <td>{{ token.tokenPrefix || token.prefix }}</td>
              <td>{{ token.createdAt }}</td>
              <td>{{ token.lastUsedAt || '-' }}</td>
              <td><button class="text-danger" @click="revokeApiToken(token.id)">撤销</button></td>
            </tr>
            <tr v-if="!settings.tokens.length"><td colspan="5">还没有 API Token。</td></tr>
          </tbody>
        </table>
      </section>
      <section v-if="settings.active === 'team'" class="account-settings-card">
        <div class="settings-card-title"><h2>团队管理</h2><p>邀请成员加入当前工作空间。</p></div>
        <div class="settings-inline-form">
          <input v-model="settings.teamEmail" placeholder="成员邮箱" />
          <select v-model="settings.teamRole"><option value="member">成员</option><option value="admin">管理员</option></select>
          <button class="btn" type="button" @click="addTeamMember">邀请成员</button>
        </div>
        <table class="settings-table">
          <thead><tr><th>邮箱</th><th>角色</th><th>状态</th><th>加入时间</th><th></th></tr></thead>
          <tbody>
            <tr v-for="member in settings.members" :key="member.id">
              <td>{{ member.email }}</td>
              <td>{{ member.role }}</td>
              <td>{{ member.status }}</td>
              <td>{{ member.createdAt }}</td>
              <td><button v-if="member.role !== 'owner'" class="text-danger" @click="removeTeamMember(member.id)">移除</button></td>
            </tr>
          </tbody>
        </table>
      </section>
      <section v-if="settings.active === 'devices'" class="account-settings-card">
        <div class="settings-card-title"><h2>设备管理</h2><p>查看并撤销已登录设备。</p></div>
        <table class="settings-table">
          <thead><tr><th>设备</th><th>IP</th><th>User Agent</th><th>最后使用</th><th>状态</th><th></th></tr></thead>
          <tbody>
            <tr v-for="device in settings.devices" :key="device.id">
              <td>{{ device.name }}</td>
              <td>{{ device.ip || '-' }}</td>
              <td>{{ device.userAgent || '-' }}</td>
              <td>{{ device.lastSeenAt }}</td>
              <td>{{ device.revokedAt ? '已撤销' : '有效' }}</td>
              <td><button v-if="!device.revokedAt" class="text-danger" @click="revokeDevice(device.id)">撤销</button></td>
            </tr>
            <tr v-if="!settings.devices.length"><td colspan="6">暂无设备记录。</td></tr>
          </tbody>
        </table>
      </section>
    </section>
  </main>
</template>
