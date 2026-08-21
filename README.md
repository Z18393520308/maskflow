# MaskFlow

MaskFlow 是一个面向图像数据集的 **AI 自动标注平台**。用户可上传图片，使用 SAM 3 进行自动分割，在 Web 工作台中修正 YOLO 标注，并导出训练数据集。

## 功能概览

- 用户注册 / 登录、忘记密码找回、账户与套餐管理
- 项目管理、图片上传与文件管理
- **SAM 3 自动分割**（文本提示、全自动识别、点提示正负点击抠图）
- **YOLO 标注工作台**（单张 / 批量自动标注、标签管理、画布缩放、保存与导出）
- 数据集导出（YOLO 目录结构 + ZIP）
- 处理任务记录、AI 调用额度
- ComputeHub 算力调度接口预留（`/v1/*`）

## 技术架构

```text
浏览器 (Vue 3)
    ↓
maskflow-api (.NET 9 WebAPI)  ←→  MinIO（对象存储，可选）
    ↓
sam-inference (FastAPI + Ultralytics SAM 3 + GPU)
```

| 组件 | 技术栈 | 默认端口 |
|------|--------|----------|
| 前端 | Vue 3 + Vite | 3010（开发）/ 3000（Docker） |
| 业务 API | ASP.NET Core 9 | 8010（开发）/ 8000（Docker） |
| AI 推理 | Python + FastAPI + SAM 3 | 8001 |
| 对象存储 | MinIO（Docker 可选） | 9000 / 9001 |

## 目录结构

```text
sam3/
├── src/
│   ├── frontend/maskflow-web/   # Vue 前端
│   ├── backend/maskflow-api/    # .NET 业务 API
│   ├── ai/sam-inference/          # Python SAM 推理服务
│   └── legacy/                    # 旧版代码备份（仅供参考）
├── 文档/                          # 产品与设计文档
├── 资源/
│   ├── 模型/sam3.pt               # SAM 3 模型（需自行放置，不纳入 Git）
│   └── 原型图/                    # UI 原型参考
├── docker-compose.yml
└── README.md
```

## 环境要求

- **Node.js** 18+（前端）
- **.NET SDK** 9.0（后端）
- **Python** 3.10+（SAM 推理，建议 CUDA 环境）
- **NVIDIA GPU + 驱动**（SAM 推理，Docker 部署时必需）
- **MySQL** 8.x（业务数据持久化，本地开发或 Docker 均可）
- **Docker Desktop**（可选，用于一键部署）

## 模型文件

SAM 3 模型 **不包含在仓库中**（体积过大）。请自行下载并放置到：

```text
资源/模型/sam3.pt
```

Docker Compose 会通过 volume 挂载该路径。若缺失，分割相关功能将无法使用。

## 快速开始（本地开发）

### 1. 启动 MySQL（本地开发）

本地需先有一个可连接的 MySQL 实例。若使用 Docker Compose，可只启动数据库：

```powershell
docker compose up -d mysql
```

默认连接串（与 `launchSettings.json` 一致）：

```text
Server=127.0.0.1;Port=3306;Database=maskflow;User ID=maskflow;Password=maskflow;Allow User Variables=true;
```

### 2. 启动业务 API

```powershell
# 业务数据持久化到 MySQL（必需）
$env:MASKFLOW_MYSQL = "Server=127.0.0.1;Port=3306;Database=maskflow;User ID=maskflow;Password=maskflow;Allow User Variables=true;"

# 已启动 MinIO 时，使用对象存储保存上传文件
$env:MASKFLOW_MINIO_ENDPOINT = "http://127.0.0.1:9000"
$env:MASKFLOW_MINIO_ACCESS_KEY = "admin"
$env:MASKFLOW_MINIO_SECRET_KEY = "admin123456"
$env:MASKFLOW_MINIO_BUCKET = "maskflow"
$env:MASKFLOW_STORAGE = "src\backend\maskflow-api\data\storage"

dotnet run --project src\backend\maskflow-api\MaskFlow.Api.csproj --urls http://127.0.0.1:8010
```

未启动 MinIO 时，可将 `MASKFLOW_MINIO_ENDPOINT` 设为空格 `" "` 以回退到本地磁盘存储。

首次启动时 API 会自动建库建表；用户、项目等业务状态仅保存在 MySQL 中，不再写入 JSON 文件。

Swagger：http://127.0.0.1:8010/swagger

### 3. 启动前端

```powershell
cd src\frontend\maskflow-web
npm install
npm run dev -- --host 127.0.0.1 --port 3010
```

访问：http://127.0.0.1:3010

前端开发服务器会将 `/api` 和 `/v1` 代理到 `http://127.0.0.1:8010`。

### 4. 启动 SAM 推理服务（可选，分割功能需要）

```powershell
cd src\ai\sam-inference
pip install -r requirements.txt
# 可选：与 API 共用内部密钥（本地不设则 SAM /api/* 不鉴权）
# $env:SAM3_INTERNAL_KEY = "your-local-dev-key"
# $env:SAM3_REQUIRE_INTERNAL_KEY = "true"
python -m uvicorn app.main:app --host 127.0.0.1 --port 8001
```

需确保 `资源/模型/sam3.pt` 已就位，且本机有可用的 CUDA GPU。

本地开发若设置了 `SAM3_INTERNAL_KEY`，请在启动 API 时使用相同值；若启用 `SAM3_REQUIRE_INTERNAL_KEY=true`，则 SAM 服务必须配置该密钥。

## Docker 部署（统一 sam3 项目）

所有服务由根目录 `docker-compose.yml` 管理，项目名为 **sam3**：

```text
sam-inference / mysql / minio / api / web
```

复制环境变量模板并按本机路径修改：

```powershell
copy .env.example .env
# 必填：MINIO_DATA_PATH、MYSQL_DATA_VOLUME、MYSQL_*、MINIO_*
# 全新安装 MySQL 前先创建数据卷：docker volume create maskflow-mysql-data
```

确保 `资源/模型/sam3.pt` 已下载，然后在项目根目录执行：

```powershell
# 启动全部服务
docker compose up -d --build

# 仅重建并更新前端 / 后端（MySQL、SAM、MinIO 不动）
docker compose up -d --build api web

# 更新分割边缘平滑 / 点提示等 AI 改动时，需重建 SAM
docker compose up -d --build sam-inference

# 同时更新前端 + API + SAM（常见发版组合）
docker compose up -d --build api web sam-inference

# 查看状态 / 日志
docker compose ps
docker compose logs -f api
```

| 服务 | 容器名 | 默认地址 |
|------|--------|----------|
| Web | maskflow-frontend | http://localhost:3000 |
| API | maskflow-api | http://localhost:8000 |
| MySQL | maskflow-mysql | localhost:3307 |
| SAM | maskflow-sam-inference | http://localhost:8001 |
| MinIO Console | maskflow-minio | http://localhost:9001 |

### 部署相关环境变量（节选）

| 变量 | 说明 | 默认建议 |
|------|------|----------|
| `ASPNETCORE_ENVIRONMENT` | `Development` 跳过部分生产密钥强校验 | 本机 `Development` |
| `MASKFLOW_PASSWORD_RESET_INLINE` | `true` 时忘记密码接口回显重置码（未接邮件） | `true`（本机/内网） |
| `MASKFLOW_ADMIN_API_KEY` | 管理接口 `X-Admin-Key` | 生产务必改成强随机值 |
| `SAM3_INTERNAL_KEY` | API ↔ SAM 内部调用密钥 | 与 SAM 容器一致 |
| `MASKFLOW_AI_DAILY_LIMIT` | AI 日额度覆盖 | 按需 |
| `MASKFLOW_BILLING_DEV_MODE` | 本地套餐测试开关 | 生产设为 `false` |

忘记密码说明：当前默认不发邮件。本机 Docker / Development 会在页面内回显重置码；生产若仍无 SMTP，需显式设置 `MASKFLOW_PASSWORD_RESET_INLINE=true`，或改为接入邮件后再关闭回显。

发版后建议强制刷新前端（Ctrl+F5）。边缘平滑与点提示改动只更新 `sam-inference` 即可；忘记密码改动需同时更新 `api` + `web`。

## 主要 API

业务接口（节选）：

```text
POST /api/auth/register
POST /api/auth/login
POST /api/auth/forgot-password
POST /api/auth/reset-password
GET  /api/projects
POST /api/files/upload
POST /api/segment
POST /api/segment/points
POST /api/annotations/auto
POST /api/annotations/points
POST /api/export/dataset
GET  /api/tasks
GET  /api/ai/quota
```

完整接口见 Swagger 文档。更多变更说明见 [`文档/修改记录.md`](文档/修改记录.md)。

## 开发说明

- 源码统一放在 `src/`，文档与模型资源分别放在 `文档/`、`资源/`
- 不要提交 `node_modules`、`bin`、`obj`、`dist`、`.vs`、`data/` 及模型权重
- 后端分层：`Controllers` → `Application` → `Infrastructure`
- 更多细节见 [`src/README.md`](src/README.md) 与 [`文档/项目说明.md`](文档/项目说明.md)

## License

暂未指定开源协议。如需对外发布，请补充 LICENSE 文件。
