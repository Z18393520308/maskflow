<template>
  <main class="mf-main page-pad work-page annotate-workbench-page">
    <section class="work-wrap annotate-shell">
      <header class="work-title annotate-title">
        <div class="work-title-icon">Y</div>
        <div>
          <h1>YOLO 标注工作台</h1>
          <p>批量上传图片，运行自动分割，在同一屏完成标签修正、确认和导出。</p>
        </div>
        <div class="save-state" :class="{ dirty: annotate.dirty }">{{ saveStateText }}</div>
      </header>

      <section class="project-bar annotate-project-bar">
        <div><strong>当前项目</strong><p>{{ selectedProject?.name || '请先选择或创建项目' }}</p></div>
        <select :value="projects.selectedId" @change="selectProject($event.target.value)">
          <option value="">选择项目</option>
          <option v-for="project in projects.rows" :key="project.id" :value="project.id">{{ project.name }} · {{ project.imageCount || 0 }} 张</option>
        </select>
        <input v-model="projects.newName" placeholder="新项目名称，例如 道路场景" @keyup.enter="createProject" />
        <button class="btn compact-btn" type="button" @click="createProject">新建项目</button>
      </section>

      <section class="annotate-topbar">
        <label class="upload-inline">
          <input type="file" multiple accept="image/*" :disabled="loading" @change="changeAnnotateFiles($event.target.files)" />
          <span>上传图片</span>
        </label>
        <label class="inline-control">置信度 {{ annotate.conf }}<input v-model="annotate.conf" type="range" min="0.01" max="0.95" step="0.01" /></label>
        <label class="inline-control annotate-default-label">
          本次默认类别
          <select v-model="annotate.defaultRunLabel" :disabled="!annotate.labels.length">
            <option value="">未分配</option>
            <option v-for="label in annotate.labels" :key="label" :value="label">{{ label }}</option>
          </select>
        </label>
        <div v-if="annotate.labels.length" class="annotate-label-legend" aria-label="标签颜色图例">
          <span v-for="label in annotate.labels" :key="label" class="annotate-legend-item">
            <i class="label-color-swatch" :style="labelSwatchStyle(label)" />
            {{ label }}
          </span>
        </div>
        <button class="btn" :disabled="loading || !annotate.current || !canRunAnnotateAi" @click="runCurrentMask">运行当前图片</button>
        <button class="btn secondary" :disabled="loading || !files.rows.length || !canRunAnnotateAi" @click="runMasks">批量运行 AI</button>
        <button class="btn secondary" :disabled="loading || !annotate.dirty" @click="saveAnnotation">保存</button>
        <button class="btn ghost" :disabled="!projects.selectedId || !annotate.labels.length" @click="createExport">导出 ZIP</button>
      </section>

      <p v-if="!annotate.labels.length" class="annotate-label-hint">请先添加至少一个项目标签，再运行 AI 自动标注。</p>

      <section class="annotate-workbench">
        <aside class="work-card image-queue-panel">
          <div class="panel-head">
            <div><h2>图片队列</h2><p>{{ files.rows.length }} 张图片</p></div>
          </div>
          <div class="queue-filters" role="tablist" aria-label="图片筛选">
            <button type="button" :class="{ active: annotate.filter === 'all' }" @click="annotate.filter = 'all'">全部</button>
            <button type="button" :class="{ active: annotate.filter === 'unannotated' }" @click="annotate.filter = 'unannotated'">未标注</button>
            <button type="button" :class="{ active: annotate.filter === 'annotated' }" @click="annotate.filter = 'annotated'">已标注</button>
          </div>
          <div class="queue-list">
            <button v-for="file in filteredFiles" :key="file.id" :class="['file-row-btn', { active: annotate.current?.id === file.id }]" type="button" @click="selectAnnotateFile(file)">
              <span>{{ file.name }}</span>
              <small>{{ formatBytes(file.size) }} · {{ file.annotated ? file.annotationCount + ' 条标注' : '未标注' }}</small>
              <b :class="['file-status-dot', { done: file.annotated }]" title="已执行自动标注"></b>
            </button>
            <div v-if="!filteredFiles.length" class="empty-note">当前筛选下没有图片。</div>
          </div>
          <p class="queue-status">{{ annotate.status }}</p>
        </aside>

        <section class="work-stage yolo-stage annotate-canvas-panel">
          <div class="canvas-topbar">
            <button class="btn secondary compact-btn" type="button" :disabled="currentFileIndex <= 0" @click="selectAdjacentFile(-1)">上一张</button>
            <span>{{ currentFileIndex >= 0 ? currentFileIndex + 1 : 0 }} / {{ files.rows.length }} · {{ annotate.current?.name || '未选择图片' }}</span>
            <button class="btn secondary compact-btn" type="button" :disabled="currentFileIndex < 0 || currentFileIndex >= files.rows.length - 1" @click="selectAdjacentFile(1)">下一张</button>
          </div>
          <div v-if="annotate.current" class="yolo-canvas">
            <div v-if="previewUrl(annotate.current)" class="yolo-image-frame" :style="yoloFrameStyle">
              <img :src="previewUrl(annotate.current)" alt="标注图片" @load="updateYoloFrame" />
              <svg class="yolo-mask-layer" viewBox="0 0 100 100" preserveAspectRatio="none">
                <polygon
                  v-for="item in annotate.annotations"
                  :key="'poly-' + item.id"
                  :points="segmentPoints(item)"
                  :class="{ active: annotate.activeId === item.id, unassigned: !item.label }"
                  :style="annotationPolygonStyle(item)"
                />
              </svg>
              <button
                v-for="item in annotate.annotations"
                :key="item.id"
                :class="['yolo-box', { active: annotate.activeId === item.id, unassigned: !item.label }]"
                :style="annotationBoxStyle(item)"
                type="button"
                @click="annotate.activeId = item.id"
              >
                {{ formatAnnotationLabel(item.label) }}
              </button>
            </div>
            <div v-else class="empty-note">正在加载图片预览...</div>
          </div>
          <div v-else class="canvas-empty">
            <strong>选择图片开始标注</strong>
            <span>左侧上传或选择图片后，可运行 AI 自动生成候选标注。</span>
          </div>
          <div class="canvas-bottombar" role="toolbar" aria-label="画布缩放">
            <button class="btn secondary compact-btn" type="button" @click="setYoloZoom(annotate.zoom - 0.1)">缩小</button>
            <button class="btn secondary compact-btn" type="button" @click="setYoloZoom(annotate.zoom + 0.1)">放大</button>
            <button class="btn ghost compact-btn" type="button" @click="resetYoloZoom">适配</button>
            <input :value="Math.round(annotate.zoom * 100)" type="range" min="25" max="300" step="5" aria-label="缩放比例" @input="setYoloZoom($event.target.value / 100)" />
            <span>{{ Math.round(annotate.zoom * 100) }}%</span>
          </div>
        </section>

        <aside class="work-card annotation-inspector">
          <div class="panel-head">
            <div>
              <h2>标注结果</h2>
              <p>{{ annotationStats.confirmed }} 人工已确认 · {{ annotationStats.pending }} 待人确认 · {{ annotationStats.unassigned }} 未分配</p>
            </div>
            <button class="btn compact-btn" type="button" :disabled="!annotate.annotations.length" @click="saveAnnotation">保存</button>
          </div>
          <div class="annotation-list">
            <section
              v-for="item in annotate.annotations"
              :key="item.id"
              :class="['annotation-row', { active: annotate.activeId === item.id, unassigned: !item.label }]"
              :style="annotationRowAccentStyle(item)"
              @click="annotate.activeId = item.id"
            >
              <span class="label-color-swatch annotation-row-swatch" :style="labelSwatchStyle(item.label)" />
              <span
                class="confirm-dot"
                :class="{ unconfirmed: !item.confirmed }"
                @click.stop="toggleAnnotationConfirmed(item.id)"
                :title="item.confirmed ? '人工已确认' : '待人确认：点击确认标签与 mask'"
              ></span>
              <select :value="item.label ?? ''" @click.stop @change="onAnnotationLabelChange(item, $event.target.value)">
                <option value="">未分配</option>
                <option v-for="label in annotate.labels" :key="label" :value="label">{{ label }}</option>
              </select>
              <small>{{ item.label ? `class ${item.classId}` : '未分配' }} · conf {{ Number(item.confidence || 1).toFixed(2) }}</small>
              <button class="btn secondary compact-btn" type="button" @click.stop="removeAnnotation(item.id)">删除</button>
            </section>
            <div v-if="!annotate.annotations.length" class="empty-note">运行 AI 后会在这里显示标注结果。</div>
          </div>

          <div class="label-manager-inline">
            <h2>标签</h2>
            <div class="settings-inline-form yolo-label-form">
              <input v-model="annotate.newLabel" placeholder="新增标签，例如 person" @keyup.enter="addAnnotateLabel" />
              <button class="btn compact-btn" type="button" @click="addAnnotateLabel">新增</button>
            </div>
            <div v-if="!annotate.labels.length" class="empty-note">添加标签后即可运行 AI 并分配类别。</div>
            <div class="label-chip-list">
              <div v-for="label in annotate.labels" :key="label" class="label-chip-row">
                <button type="button" class="label-chip" :style="labelChipStyle(label)" @click="applyLabelToActive(label)">
                  <i class="label-color-swatch" :style="labelSwatchStyle(label)" />
                  {{ label }}
                </button>
                <template v-if="annotate.pendingDeleteLabel === label">
                  <select v-model="annotate.labelDeleteReplace" class="label-delete-replace">
                    <option value="">未分配</option>
                    <option v-for="other in annotate.labels.filter((item) => item !== label)" :key="other" :value="other">{{ other }}</option>
                  </select>
                  <button class="btn compact-btn" type="button" :disabled="loading" @click="confirmDeleteAnnotateLabel">确认</button>
                  <button class="btn secondary compact-btn" type="button" @click="cancelDeleteAnnotateLabel">取消</button>
                </template>
                <button
                  v-else
                  class="label-delete-btn"
                  type="button"
                  :disabled="loading"
                  @click="beginDeleteAnnotateLabel(label)"
                >
                  删除
                </button>
              </div>
            </div>
          </div>

          <div class="active-object-card">
            <h2>当前目标</h2>
            <p v-if="activeAnnotation">
              {{ formatAnnotationLabel(activeAnnotation.label) }}
              · {{ activeAnnotation.label ? `class ${activeAnnotation.classId}` : '未分配' }}
              · {{ activeAnnotation.confirmed ? '人工已确认' : '待人确认' }}
            </p>
            <p v-else>选择一个标注目标后显示属性。</p>
            <button class="btn secondary" type="button" :disabled="!annotate.annotations.some((item) => item.label)" @click="downloadCurrentTxt">导出当前 TXT</button>
          </div>
        </aside>
      </section>
    </section>
  </main>
</template>

<script setup>
import { inject } from "vue";

const annotate = inject("annotate");
const projects = inject("projects");
const selectedProject = inject("selectedProject");
const files = inject("files");
const filteredFiles = inject("filteredFiles");
const loading = inject("loading");
const formatBytes = inject("formatBytes");
const selectAnnotateFile = inject("selectAnnotateFile");
const runCurrentMask = inject("runCurrentMask");
const runMasks = inject("runMasks");
const saveAnnotation = inject("saveAnnotation");
const removeAnnotation = inject("removeAnnotation");
const annotationBoxStyle = inject("annotationBoxStyle");
const segmentPoints = inject("segmentPoints");
const yoloFrameStyle = inject("yoloFrameStyle");
const updateYoloFrame = inject("updateYoloFrame");
const addAnnotateLabel = inject("addAnnotateLabel");
const beginDeleteAnnotateLabel = inject("beginDeleteAnnotateLabel");
const confirmDeleteAnnotateLabel = inject("confirmDeleteAnnotateLabel");
const cancelDeleteAnnotateLabel = inject("cancelDeleteAnnotateLabel");
const applyLabelToActive = inject("applyLabelToActive");
const applyAnnotationLabel = inject("applyAnnotationLabel");
const toggleAnnotationConfirmed = inject("toggleAnnotationConfirmed");
const changeAnnotateFiles = inject("changeAnnotateFiles");
const previewUrl = inject("previewUrl");
const downloadCurrentTxt = inject("downloadCurrentTxt");
const selectProject = inject("selectProject");
const createProject = inject("createProject");
const createExport = inject("createExport");
const currentFileIndex = inject("currentFileIndex");
const activeAnnotation = inject("activeAnnotation");
const annotationStats = inject("annotationStats");
const saveStateText = inject("saveStateText");
const setYoloZoom = inject("setYoloZoom");
const resetYoloZoom = inject("resetYoloZoom");
const selectAdjacentFile = inject("selectAdjacentFile");
const canRunAnnotateAi = inject("canRunAnnotateAi");
const formatAnnotationLabel = inject("formatAnnotationLabel");
const annotationPolygonStyle = inject("annotationPolygonStyle");
const labelChipStyle = inject("labelChipStyle");
const labelSwatchStyle = inject("labelSwatchStyle");
const annotationRowAccentStyle = inject("annotationRowAccentStyle");

function onAnnotationLabelChange(item, value) {
  applyAnnotationLabel(item, value);
}
</script>
