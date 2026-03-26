const fs = require("node:fs");
const path = require("node:path");
const readline = require("node:readline");
const { createRequire } = require("node:module");
const { pathToFileURL } = require("node:url");
const { execFileSync } = require("node:child_process");

const SAMPLE_RATE = 16000;
const FEATURE_DIM = 80;
const NUM_THREADS = 4;

let ensureModels = null;
let getModelPath = null;
let sherpaOnnx = null;
let recognizer = null;

function logErr(msg) {
  process.stderr.write(`[asr-worker] ${msg}\n`);
}

function emit(obj) {
  process.stdout.write(`${JSON.stringify(obj)}\n`);
}

function resolveColiPackageDirFromEnv() {
  const coliExePath = process.env.COLI_EXE_PATH;
  if (!coliExePath) return null;
  const npmBinDir = path.dirname(coliExePath);
  const pkgDir = path.join(npmBinDir, "node_modules", "@marswave", "coli");
  const pkgJson = path.join(pkgDir, "package.json");
  return fs.existsSync(pkgJson) ? pkgDir : null;
}

function resolveColiPackageDirFromNpmRoot() {
  try {
    const npmRoot = execFileSync("npm", ["root", "-g"], {
      stdio: ["ignore", "pipe", "pipe"],
      encoding: "utf8",
      windowsHide: true,
    }).trim();
    if (!npmRoot) return null;
    const pkgDir = path.join(npmRoot, "@marswave", "coli");
    const pkgJson = path.join(pkgDir, "package.json");
    return fs.existsSync(pkgJson) ? pkgDir : null;
  } catch {
    return null;
  }
}

async function bootstrapModules() {
  const pkgDir =
    resolveColiPackageDirFromEnv() ||
    resolveColiPackageDirFromNpmRoot();

  if (!pkgDir) {
    throw new Error("Cannot locate @marswave/coli package directory");
  }

  const coliEntry = path.join(pkgDir, "distribution", "index.js");
  const coliPkg = await import(pathToFileURL(coliEntry).href);
  ensureModels = coliPkg.ensureModels;
  getModelPath = coliPkg.getModelPath;
  const scopedRequire = createRequire(coliEntry);
  sherpaOnnx = scopedRequire("sherpa-onnx-node");
}

async function initRecognizer() {
  await bootstrapModules();
  const t0 = Date.now();
  await ensureModels(["sensevoice"]);
  const modelDir = getModelPath("sensevoice");
  recognizer = new sherpaOnnx.OfflineRecognizer({
    featConfig: { sampleRate: SAMPLE_RATE, featureDim: FEATURE_DIM },
    modelConfig: {
      senseVoice: {
        model: path.join(modelDir, "model.int8.onnx"),
        useInverseTextNormalization: 1,
      },
      tokens: path.join(modelDir, "tokens.txt"),
      numThreads: NUM_THREADS,
      provider: "cpu",
      debug: 0,
    },
  });
  logErr(`ready in ${Date.now() - t0}ms`);
}

function wavToFloat32(wavData) {
  if (!wavData || wavData.byteLength < 44) return null;
  const int16 = new Int16Array(
    wavData.buffer,
    wavData.byteOffset + 44,
    (wavData.byteLength - 44) / 2
  );
  const float32 = new Float32Array(int16.length);
  for (let i = 0; i < int16.length; i++) {
    float32[i] = int16[i] / 32768.0;
  }
  return float32;
}

function decodeFile(audioPath) {
  const t0 = Date.now();
  const wavData = fs.readFileSync(audioPath);
  const samples = wavToFloat32(wavData);
  if (!samples) return { text: "", durMs: Date.now() - t0 };

  const stream = recognizer.createStream();
  stream.acceptWaveform({ sampleRate: SAMPLE_RATE, samples });
  recognizer.decode(stream);
  const result = recognizer.getResult(stream);
  return { text: (result.text || "").trim(), durMs: Date.now() - t0 };
}

async function main() {
  try {
    await initRecognizer();
    emit({ type: "ready" });
  } catch (err) {
    emit({ type: "fatal", error: err && err.message ? err.message : String(err) });
    process.exit(1);
    return;
  }

  const rl = readline.createInterface({
    input: process.stdin,
    crlfDelay: Infinity,
  });

  rl.on("line", (line) => {
    if (!line || !line.trim()) return;
    let req;
    try {
      req = JSON.parse(line);
    } catch {
      emit({ id: -1, ok: false, error: "Invalid JSON request" });
      return;
    }

    const id = Number(req.id);
    const op = req.op;

    if (op === "shutdown") {
      emit({ id, ok: true });
      process.exit(0);
      return;
    }

    if (op !== "transcribe") {
      emit({ id, ok: false, error: `Unsupported op: ${op}` });
      return;
    }

    const audioPath = req.audioPath;
    if (!audioPath || !fs.existsSync(audioPath)) {
      emit({ id, ok: false, error: "Audio file does not exist" });
      return;
    }

    try {
      const decoded = decodeFile(audioPath);
      emit({ id, ok: true, text: decoded.text, durMs: decoded.durMs });
    } catch (err) {
      emit({
        id,
        ok: false,
        error: err && err.message ? err.message : String(err),
      });
    }
  });

  rl.on("close", () => process.exit(0));
}

main().catch((err) => {
  emit({ type: "fatal", error: err && err.message ? err.message : String(err) });
  process.exit(1);
});
