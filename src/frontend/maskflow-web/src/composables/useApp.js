// Shared application state and functions
import { computed, nextTick, onMounted, reactive, ref } from "vue";
import { apiFetch, authHeaders, clearSession, formatBytes, saveSession, session, user as currentUser } from "../lib/api";

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

export const path = ref(window.location.pathname);
export const page = computed(() => routes[path.value] || "home");
export const account = ref(currentUser());
export const message = ref("");
export const loading = ref(false);

export const auth = reactive({ mode: new URLSearchParams(location.search).get("mode") || "login", email: "", username: "", password: "" });
export const dashboard = reactive({ projects: [], tasks: [], quota: null });
export const files = reactive({ rows: [], selected: null });
export const records = reactive({ rows: [] });
export const projects = reactive({ rows: [], selectedId: "", newName: "", status: "" });
export const selectedProject = computed(() => projects.rows.find((item) => item.id === projects.selectedId) || null);
export const segment = reactive({ file: null, preview: "", prompt: "", conf: 0.25, overlay: "", overlays: {}, activeOverlay: "all", categories: [], status: "准备就绪", warning: "", mode: "", count: 0 });
export const annotate = reactive({
  selected: null, fileId: null, current: null, tab: "workspace", preview: "", frame: { width: 0, height: 0 },
  annotations: [], activeId: "", width: 0, height: 0, conf: 0.25, labels: ["object"], newLabel: "", status: "请选择或上传图片"
});
export const settings = reactive({
  active: "profile", username: "", phone: "", currentPassword: "", newPassword: "", confirmPassword: "",
  notifications: { emailTask: true, emailBilling: true, browserNotice: true, weeklyReport: false },
  tokenName: "", tokenValue: "", tokens: [], teamEmail: "", teamRole: "member", members: [], devices: []
});
export const settingsTabs = [
  ["profile", "个人信息"], ["password", "修改密码"], ["notifications", "通知设置"],
  ["tokens", "API Token"], ["team", "团队管理"], ["devices", "设备管理"]
];
export const billingPlans = [
  { id: "free", name: "Free", price: 0, storage: "10G", ai: "每日 50 次", audience: "个人体验", features: ["基础标注工具", "单图 AI 分割", "YOLO 数据集导出", "文件管理"] },
  { id: "pro", name: "Pro", price: 49, storage: "50G", ai: "每日 1000 次", audience: "专业用户", featured: true, features: ["批量自动标注", "高级标注工具", "API Token", "团队协作"] },
  { id: "team", name: "Team", price: 299, storage: "500G", ai: "高额度", audience: "团队协作", features: ["高优先级处理", "团队成员管理", "专属技术支持", "自定义导出"] }
];
export const billingExplain = [
  ["存储", "存储空间", "用于存储上传图片、视频、标注数据和导出结果。"],
  ["AI", "AI 处理次数", "包括自动检测、智能分割、人工校正辅助等 AI 功能调用次数。"],
  ["标注", "标注工具", "不同套餐提供不同的标注工具和高级能力。"],
  ["团队", "团队协作", "支持团队成员管理、任务分配与协作标注。"]
];
export const billingFaqs = ["套餐可以随时升级或降级吗？", "AI 处理次数用完后怎么办？", "存储空间可以单独购买吗？", "如何开具发票？"];
export const homeFeatures = [
  ["AI", "AI 自动分割", "高精度分割，节省时间"], ["批", "批量处理", "支持批量图片自动处理"],
  ["标", "标注工具", "丰富的标注与编辑工具"], ["集", "数据集导出", "支持 YOLO 格式导出"],
  ["云", "云端加速", "按需算力，权限管理"], ["安", "安全可靠", "隐私保护，权限管理"]
];

export function go(to) {
  history.pushState({}, "", to);
  path.value = window.location.pathname;
  message.value = "";
  if (needsLogin(page.value) && !account.value) go("/auth.html");
  refreshPage();
}

export function needsLogin(name) {
  return !["home", "auth", "segment"].includes(name);
}

export function logout() {
  clearSession();
  account.value = null;
  go("/index.html");
}