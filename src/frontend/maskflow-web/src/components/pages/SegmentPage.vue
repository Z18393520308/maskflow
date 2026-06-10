<script setup>
import { inject } from "vue";

const segment = inject("segment");
const loading = inject("loading");
const runSegment = inject("runSegment");
const selectSegmentFile = inject("selectSegmentFile");
const showSegmentOverlay = inject("showSegmentOverlay");
</script>

<template>
  <main class="mf-main page-pad work-page">
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
          <p v-if="segment.warning" class="segment-warning">自动分类暂不可用，已先返回分割结果。可输入 person、car 等提示词获得类别结果。</p>
          <div class="overlay-list">
            <button v-if="segment.overlays.all" :class="['overlay-row', { active: segment.activeOverlay === 'all' }]" type="button" @click="showSegmentOverlay('all')"><span>全部目标</span><b>{{ segment.count }}</b></button>
            <button v-for="cat in segment.categories" :key="cat.id" :class="['overlay-row', { active: segment.activeOverlay === cat.id }]" type="button" @click="showSegmentOverlay(cat.id)"><span>{{ cat.label }}</span><b>{{ cat.count }}</b></button>
          </div>
          <p v-if="!segment.categories.length">运行后将在这里显示识别类别。</p>
        </aside>
      </section>
    </section>
  </main>
</template>
