import { ensureModels, getModelPath } from '@marswave/coli';

async function test() {
  console.log('--- 验证 ASR 核心集成 ---');
  
  try {
    // 1. 验证模型确保逻辑
    console.log('1. 正在检查模型环境 (含 sherpa-onnx-node 初始化)...');
    await ensureModels(['sensevoice']);
    console.log('2. [成功] ASR 模型环境已就绪。');

    // 2. 检查物理模型路径
    // 如果 getModelPath 未导出，这里捕获错误并提示
    try {
        const modelPath = getModelPath('sensevoice');
        console.log(`3. 模型存放路径: ${modelPath}`);
    } catch {
        console.log('3. [提示] getModelPath 未公开导出。');
    }
    
    console.log('--- 验证成功! 内芯已打通 ---');
  } catch (err) {
    console.error(`验证失败: ${err.message}`);
  }
}

test();
