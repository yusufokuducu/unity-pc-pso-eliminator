# Unity PC DX12 & Vulkan PSO Stutter Eliminator

[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B%20%7C%20Unity%206-black?logo=unity)](https://unity.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Solves one of the most notorious problems in modern PC game development (`DirectX 12` and `Vulkan`): **Shader Compilation / Pipeline State Object (PSO) Micro-Stutters**.

---

## Why Standard `ShaderVariantCollection.WarmUp()` Fails on PC

1. **Monolithic Loading Freezes**: Calling `WarmUp()` synchronously on a bloated `ShaderVariantCollection` freezes the main thread for tens of seconds (or minutes), causing Windows "Not Responding" hangs.
2. **Incomplete Hardware PSO Binding**: On DirectX 12 and Vulkan, a hardware PSO requires the compiled shader bytecode **plus** the vertex input layout, blend state, depth-stencil state, and render target format. Simply warming up a `ShaderVariantCollection` often fails to bind the full pipeline state until the first actual draw call occurs in gameplay.

---

## How `unity-pc-pso-eliminator` Fixes It

1. **Lean Project Variant Harvester (`SceneShaderVariantHarvester`)**
   - Crawls active project materials and extracts only the exact `Shader` + `PassType` + `ShaderKeyword[]` permutations actually used, generating a clean `ShaderVariantCollection` and `PsoWarmupManifest`.
2. **Two-Stage Frame-Budgeted Async Prewarmer (`AsyncPsoWarmupOrchestrator`)**
   - **Stage 1**: Slices `ShaderVariantCollection.WarmUpProgressively()` across frames using a high-resolution `Stopwatch` budget (e.g., `3.5 ms/frame`) so loading screen animations stay at 60+ FPS.
   - **Stage 2**: Executes an offscreen `CommandBuffer` (`4x4` `R8G8B8A8_UNorm` + `24-bit Depth` target) that issues micro triangle draw calls across each harvested `Material`'s passes to force the DX12/Vulkan driver to finalize hardware PSOs before gameplay starts.
3. **Runtime PSO Hitch Detector**
   - Continuously compares frame times against a rolling exponential moving average and logs any compilation hitch spike (`PsoHitchEvent`) with frame index, spike ratio, and graphics API.

---

## Installation (Unity Package Manager)

Add via Git URL in **Window -> Package Manager -> + -> Add package from git URL...**:

```text
https://github.com/yusufokuducu/unity-pc-pso-eliminator.git
```

---

## Usage

1. Open **Window -> Analysis -> PC DX12 & Vulkan PSO Stutter Eliminator**.
2. Click **Harvest Project Materials & Build PSO Manifest** to generate `Assets/PsoWarmupManifest.asset` and `Assets/HarvestedShaderVariants.shadervariants`.
3. Click **Add Async Warmup Orchestrator to Scene** on your bootstrap or loading scene, assign the manifest, and subscribe to `OnWarmupProgress` / `OnWarmupCompleted`.

## License

MIT License — see [LICENSE](LICENSE).
