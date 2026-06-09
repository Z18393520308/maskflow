<template>
  <main v-if="page === 'auth'" class="auth-redesign">
    <section class="auth-hero-panel">
      <a class="auth-brand" href="#" @click.prevent="go('/index.html')"><span class="logo-mark">M</span><strong>MaskFlow</strong></a>
      <div>
        <p class="home-badge">AI 自动标注平台</p>
        <h1>让标注、复核和导出都在一个工作台完成</h1>
        <p>登录后即可管理项目、上传图片、运行自动分割、复核 YOLO 标注，并把结果导出成训练可用的数据集。</p>
      </div>
      <div class="auth-preview-mini">
        <div><span></span><b>AI 自动分割</b><em>38 / 50 次</em></div>
        <div><span></span><b>项目图片</b><em>128 张</em></div>
        <div><span></span><b>导出格式</b><em>YOLO txt</em></div>
      </div>
    </section>
    <section class="auth-card">
      <div class="auth-card-head"><p>{{ auth.mode === 'register' ? '创建账户' : '账户登录' }}</p><h2>{{ auth.mode === 'register' ? '开始使用 MaskFlow' : '欢迎回来' }}</h2></div>
      <div class="auth-switch" role="tablist" aria-label="登录注册切换"><button :class="{ active: auth.mode === 'login' }" type="button" @click="auth.mode = 'login'">登录</button><button :class="{ active: auth.mode === 'register' }" type="button" @click="auth.mode = 'register'">注册</button></div>
      <form class="auth-form" @submit.prevent="submitAuth">
        <div class="form-row"><label>邮箱</label><input v-model="auth.email" type="email" autocomplete="email" placeholder="name@example.com" required /></div>
        <div v-if="auth.mode === 'register'" class="form-row"><label>用户名</label><input v-model="auth.username" autocomplete="name" placeholder="MaskFlow User" /></div>
        <div class="form-row"><label>密码</label><input v-model="auth.password" type="password" :autocomplete="auth.mode === 'register' ? 'new-password' : 'current-password'" placeholder="请输入密码" required /></div>
        <div class="auth-options"><label><input type="checkbox" /> 记住我</label><a href="#">忘记密码？</a></div>
        <p v-if="message" class="form-error">{{ message }}</p>
        <button class="auth-submit" type="submit" :disabled="loading">{{ loading ? '处理中...' : (auth.mode === 'register' ? '注册并进入控制台' : '登录') }}</button>
      </form>
      <p class="auth-footnote">{{ auth.mode === 'register' ? '已有账户？' : '还没有账户？' }} <a href="#" @click.prevent="auth.mode = auth.mode === 'register' ? 'login' : 'register'">{{ auth.mode === 'register' ? '去登录' : '免费注册' }}</a></p>
    </section>
  </main>
</template>

<script setup>
import { inject } from "vue";

const page = inject("page");
const auth = inject("auth");
const go = inject("go");
const message = inject("message");
const loading = inject("loading");
const submitAuth = inject("submitAuth");
</script>
