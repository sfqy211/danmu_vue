<template>
  <div class="ai-analysis-container">
    <div v-if="loading" class="loading-state">
      <el-icon class="is-loading"><Loading /></el-icon>
      <p>AI 正在分析弹幕数据，请稍候...</p>
    </div>

    <div v-else-if="analysisResult" class="result-content">
      <div class="markdown-body" v-html="formattedResult"></div>
    </div>

    <div v-else class="empty-state">
      <el-empty description="暂无分析结果">
        <el-button type="primary" @click="startAnalysis">开始 AI 分析</el-button>
      </el-empty>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { useDanmakuStore } from '../stores/danmakuStore';
import { analyzeSession } from '../api/danmaku';
import { Loading } from '@element-plus/icons-vue';
import { ElMessage } from 'element-plus';

const store = useDanmakuStore();
const loading = ref(false);
const analysisResult = ref('');

const formattedResult = computed(() => {
  return analysisResult.value;
});

const startAnalysis = async () => {
  if (!store.currentSession?.id) return;
  
  loading.value = true;
  try {
    const res = await analyzeSession(store.currentSession.id);
    analysisResult.value = res.analysis;
  } catch (error) {
    console.error('Analysis failed:', error);
    ElMessage.error('分析失败，请稍后重试');
  } finally {
    loading.value = false;
  }
};
</script>

<style scoped>
.ai-analysis-container {
  padding: 20px;
  min-height: 400px;
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 300px;
  color: var(--text-secondary);
}

.loading-state .el-icon {
  font-size: 40px;
  margin-bottom: 16px;
  color: var(--el-color-primary);
}

.result-content {
  line-height: 1.6;
  color: var(--text-primary);
  white-space: pre-wrap;
  font-family: inherit;
}

.markdown-body {
  font-size: 15px;
}

</style>
