<template>
  <main class="mf-main page-pad work-page">
    <section class="work-wrap">
      <header class="work-title">
        <div class="work-title-icon">Y</div>
        <div><h1>YOLO 标注</h1><p>多图上传后手动触发自动标注，支持查看、修正、保存和导出 YOLO 数据集。</p></div>
      </header>
      <section class="project-bar">
        <div><strong>当前项目</strong><p>{{ selectedProject?.name || '请先选择或创建项目' }}</p></div>
        <select v-model="projects.selectedId" @change="selectProject(projects.selectedId)">
          <option value="">选择项目</option>
          <option v-for="project in projects.rows" :key="project.id" :value="project.id">{{ project.name }} · {{ project.imageCount || 0 }} 张</option>
        </select>
        <input v-model="projects.newName" placeholder="新项目名称，例如 药瓶分类" @keyup.enter="createProject" />
        <button class="btn compact-btn" type="button" @click="createProject">新增项目</button>
      </section>
      <nav class="billing-proto-tabs work-tabs">
        <button :class="{ active: annotate.tab === 'workspace' }" type="button" @click="annotate.tab = 'workspace'">标注工作台</button>
        <button :class="{ active: annotate.tab === 'labels' }" type="button" @click="annotate.tab = 'labels'">标签管理</button>
      </nav>
      <section class="work-toolbar">
        <input type="file" multiple accept="image/*" :disabled="loading" @change="changeAnnotateFiles($event.target.files)" />
        <label class="inline-control">置信度 {{ annotate.conf }}<input v-model="annotate.conf" type="range" min="0.01" max="0.95" step="0.01" /></label>
        <button class="btn" :disabled="loading || !annotate.current" @click="runCurrentMask">分割标注当前图片</button>
        <button class="btn secondary" :disabled="loading || !files.rows.length" @click="runMasks">分割标注全部图片</button>
        <button class="btn secondary" :disabled="!annotate.annotations.length" @click="downloadCurrentTxt">导出当前 TXT</button>
        <button class="btn secondary" @click="createExport">下载数据集 ZIP</button>
      </section>
      <section v-if="annotate.tab === 'workspace'" class="work-tool-grid yolo-tool-grid">
        <aside class="work-card yolo-file-panel">
          <h2>图片列表</h2>
          <p>{{ annotate.status }}</p>
          <p v-if="account">空间：{{ formatBytes(account.usedBytes) }} / {{ formatBytes(account.quotaBytes) }}</p>
          <button v-for="file in files.rows" :key="file.id" :class="['file-row-btn', { active: annotate.current?.id === file.id }]" type="button" @click="selectAnnotateFile(file)">
            <span>{{ file.name }}</span>
            <small>{{ formatBytes(file.size) }} · {{ file.annotated ? file.annotationCount + ' 条' : '未标注' }}</small>
            <b class="label-delete" role="button" tabindex="0" @click.stop="deleteFile(file.id)" @keydown.enter.stop.prevent="deleteFile(file.id)">删除</b>
          </button>
          <p v-if="!files.rows.length">暂无图片，请先上传。</p>
        </aside>
        <section class="work-stage yolo-stage">
          <div v-if="annotate.current" class="yolo-canvas">
            <div v-if="previewUrl(annotate.current)" class="yolo-image-frame" :style="yoloFrameStyle">
              <img :src="previewUrl(annotate.current)" alt="标注图片" @load="updateYoloFrame" />
              <svg class="yolo-mask-layer" viewBox="0 0 100 100" preserveAspectRatio="none">
                <polygon v-for="item in annotate.annotations" :key="'poly-' + item.id" :points="segmentPoints(item)" :class="{ active: annotate.activeId === item.id }" />
              </svg>
              <button v-for="item in annotate.annotations" :key="item.id" :class="['yolo-box', { active: annotate.activeId === item.id }]" :style="annotationBoxStyle(item)" type="button" @click="annotate.activeId = item.id">
                {{ item.label }}
              </button>
            </div>
            <div v-else>正在加载图片预览...</div>
          </div>
          <div v-else>上传或选择图片后开始 YOLO 标注</div>
        </section>
        <aside class="work-card yolo-result-panel">
          <div class="result-panel-head">
            <div><h2>标注结果</h2><p>数量：{{ annotate.annotations.length }}</p></div>
            <button class="btn compact-btn" type="button" :disabled="!annotate.annotations.length" @click="saveAnnotation">保存标注</button>
          </div>
          <div class="annotation-list">
            <section v-for="item in annotate.annotations" :key="item.id" :class="['annotation-row', { active: annotate.activeId === item.id }]" @click="annotate.activeId = item.id">
              <span class="confirm-dot" :class="{ unconfirmed: !item.confirmed }" @click.stop="toggleAnnotationConfirmed(item.id)" :title="item.confirmed ? '已确认' : '未确认，点击确认'"></span>
              <select v-model="item.label" @click.stop @change="syncAnnotationLabels">
                <option v-for="label in annotate.labels" :key="label" :value="label">{{ label }}</option>
              </select>
              <small>class {{ item.classId }} · conf {{ Number(item.confidence || 1).toFixed(2) }}</small>
              <small v-if="item.bbox">bbox {{ Number(item.bbox.cx).toFixed(3) }}, {{ Number(item.bbox.cy).toFixed(3) }}</small>
              <button class="btn secondary" type="button" @click.stop="removeAnnotation(item.id)">删除</button>
            </section>
          </div>
          <p v-if="!annotate.annotations.length">点击一键自动标注后将在这里显示结果。</p>
        </aside>
      </section>
      <section v-else class="work-bottom yolo-label-layout">
        <article class="work-card">
          <h2>本批次标签</h2>
          <p>这些标签会用于当前项目“{{ selectedProject?.name || '未选择项目' }}”下所有图片的标注结果。</p>
          <div class="settings-inline-form yolo-label-form">
            <input v-model="annotate.newLabel" placeholder="新增标签，例如 person" @keyup.enter="addAnnotateLabel" />
            <button class="btn compact-btn" type="button" @click="addAnnotateLabel">新增</button>
          </div>
          <section v-for="label in annotate.labels" :key="label" class="label-row">
            <button class="label-name-btn" type="button" @click="applyLabelToActive(label)">{{ label }}</button>
            <span>应用到选中目标</span>
            <button class="label-delete-btn" type="button" :disabled="label === 'object'" @click="deleteAnnotateLabel(label)">{{ label === 'object' ? '默认' : '删除' }}</button>
          </section>
        </article>
        <article class="work-card wide">
          <h2>当前图片标注</h2>
          <table>
            <tbody>
              <tr v-for="item in annotate.annotations" :key="item.id" :class="{ active: annotate.activeId === item.id }" @click="annotate.activeId = item.id">
                <td>{{ item.id }}</td>
                <td><span class="confirm-dot" :class="{ unconfirmed: !item.confirmed }" @click.stop="toggleAnnotationConfirmed(item.id)" :title="item.confirmed ? '已确认' : '未确认，点击确认'"></span><select v-model="item.label" @change="syncAnnotationLabels"><option v-for="label in annotate.labels" :key="label" :value="label">{{ label }}</option></select></td>
                <td>class {{ item.classId }}</td>
                <td>{{ Number(item.confidence || 1).toFixed(2) }}</td>
              </tr>
              <tr v-if="!annotate.annotations.length"><td colspan="4">当前图片还没有标注结果。</td></tr>
            </tbody>
          </table>
        </article>
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
const account = inject("account");
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
const deleteAnnotateLabel = inject("deleteAnnotateLabel");
const applyLabelToActive = inject("applyLabelToActive");
const syncAnnotationLabels = inject("syncAnnotationLabels");
const toggleAnnotationConfirmed = inject("toggleAnnotationConfirmed");
const changeAnnotateFiles = inject("changeAnnotateFiles");
const previewUrl = inject("previewUrl");
const downloadCurrentTxt = inject("downloadCurrentTxt");
const selectProject = inject("selectProject");
const createProject = inject("createProject");
const deleteFile = inject("deleteFile");
const createExport = inject("createExport");
</script>
