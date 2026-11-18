# Unity PC DX12 & Vulkan PSO Stutter Eliminator

[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B%20%7C%20Unity%206-black?logo=unity)](https://unity.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Solves one of the most notorious problems in modern PC game development (`DirectX 12` and `Vulkan`): **Shader Compilation / Pipeline State Object (PSO) Micro-Stutters**.

---

## Why Standard `ShaderVariantCollection.WarmUp()` Fails on PC

1. **Monolithic Loading Freezes**: Calling `WarmUp()` synchronously on a bloated `ShaderVariantCollection` freezes the main thread for tens of seconds (or minutes), triggering Windows "Not Responding" hangs.
2. **Incomplete Hardware PSO Binding**: On DirectX 12 and Vulkan, a hardware PSO requires the compiled shader bytecode **plus** the vertex input layout, blend state, depth-stencil state, and exact render target format (`R8G8B8A8_UNorm` SDR vs `R16G16B16A16_SFloat` HDR vs `B10G11R11_UFloatPack32`). Simply warming up a `ShaderVariantCollection` often fails to finalize the hardware pipeline state until the first actual draw call occurs in gameplay.

---

## How `unity-pc-pso-eliminator` Fixes It

1. **Lean Project Variant Harvester & Canonical Deduplicator (`SceneShaderVariantHarvester` & `PsoWarmupManifest`)**
   - Crawls active project materials and extracts only the exact `Shader` + `PassType` + canonically sorted `ShaderKeyword[]` permutations actually used, generating a deduplicated `ShaderVariantCollection` and `PsoWarmupManifest`.
   - Computes a deterministic **64-bit FNV-1a Driver & Manifest Fingerprint** (`ComputeDriverAndManifestFingerprint`) combining `graphicsDeviceName`, `graphicsDeviceVersion`, `graphicsDeviceType`, and all harvested variant keys so you can detect GPU driver updates that invalidate driver shader caches.
2. **Two-Stage Frame-Budgeted Async Prewarmer (`AsyncPsoWarmupOrchestrator`)**
   - **Stage 1**: Slices `ShaderVariantCollection.WarmUpProgressively()` across frames using a high-resolution `Stopwatch` budget (e.g., `3.5 ms/frame`) so loading screen animations stay at 60–120+ FPS.
   - **Stage 2**: Executes an offscreen `CommandBuffer` across configurable **RenderTarget Format Profiles** (`SdrR8G8B8A8_UNorm`, `HdrR16G16B16A16_SFloat`, `HdrB10G11R11_UFloatPack32`) that issues micro triangle draw calls across each harvested `Material`'s passes to force the DX12/Vulkan driver to finalize hardware PSOs before gameplay starts.
3. **Adaptive Runtime PSO Hitch Detector**
   - Establishes a rolling exponential moving average from live frame timings and logs compilation hitch spikes (`PsoHitchEvent`) with frame index, spike ratio, and graphics API.
4. **Headless CI / Batchmode Harvesting**
   ```bash
   unity -batchmode -quit -projectPath . -executeMethod YusufOkuducu.PcPsoEliminator.Editor.SceneShaderVariantHarvester.RunCiHarvest
   ```

---

## Installation (Unity Package Manager)

Add via Git URL in **Window -> Package Manager -> + -> Add package from git URL...**:

```text
https://github.com/yusufokuducu/unity-pc-pso-eliminator.git
```

---

## Running Standalone Unit Tests (.NET CLI)

```bash
dotnet test DotNetTests~/PcPsoEliminator.Tests.csproj
```

## License

MIT License — see [LICENSE](LICENSE).
