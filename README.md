# Unity PC DX12 & Vulkan PSO Stutter Eliminator

[![CI](https://github.com/yusufokuducu/unity-pc-pso-eliminator/actions/workflows/ci.yml/badge.svg)](https://github.com/yusufokuducu/unity-pc-pso-eliminator/actions/workflows/ci.yml)
[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B%20%7C%20Unity%206-black?logo=unity)](https://unity.com)
[![Version](https://img.shields.io/badge/version-1.2.0-brightgreen.svg)](CHANGELOG.md)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Solves one of the most notorious problems in modern PC game development (`DirectX 12` and `Vulkan`): **Shader Compilation / Pipeline State Object (PSO) Micro-Stutters**.

---

## Why Standard `ShaderVariantCollection.WarmUp()` Fails on PC

1. **Monolithic Loading Freezes**: Calling `WarmUp()` synchronously on a bloated `ShaderVariantCollection` freezes the main thread for tens of seconds (or minutes), triggering Windows "Not Responding" hangs.
2. **Incomplete Hardware PSO Binding**: On DirectX 12 and Vulkan, a hardware PSO requires the compiled shader bytecode **plus** the vertex input layout (static vs skinned `BoneWeight`), blend state, depth-stencil state, MSAA sample count, and exact render target format (`R8G8B8A8_UNorm` SDR vs `R16G16B16A16_SFloat` HDR vs `B10G11R11_UFloatPack32`). Simply warming up a `ShaderVariantCollection` often fails to finalize the hardware pipeline state until the first actual draw call occurs in gameplay.

---

## How `unity-pc-pso-eliminator` Fixes It

1. **Lean Project Variant Harvester & Canonical Deduplicator (`SceneShaderVariantHarvester` & `PsoWarmupManifest`)**
   - Crawls active project materials and extracts only the exact `Shader` + `PassType` + canonically sorted `ShaderKeyword[]` permutations actually used, generating a deduplicated `ShaderVariantCollection` and `PsoWarmupManifest`.
   - Computes a deterministic **64-bit FNV-1a Driver & Manifest Fingerprint** (`ComputeDriverAndManifestFingerprint`) combining `graphicsDeviceName`, `graphicsDeviceVersion`, `graphicsDeviceType`, `renderTargetProfiles`, `msaaSampleCounts`, and all harvested variant keys so you can detect GPU driver updates that invalidate driver shader caches.
2. **Two-Stage Frame-Budgeted Async Prewarmer (`AsyncPsoWarmupOrchestrator`)**
   - **Stage 1**: Slices `ShaderVariantCollection.WarmUpProgressively()` across frames using a high-resolution `Stopwatch` budget (e.g., `3.5 ms/frame`) so loading screen animations stay at 60–120+ FPS.
   - **Stage 2**: Executes an offscreen `CommandBuffer` across configurable **RenderTarget Format Profiles** (`SdrR8G8B8A8_UNorm`, `HdrR16G16B16A16_SFloat`, `HdrB10G11R11_UFloatPack32`) and **MSAA Sample Counts** (`1x`, `2x`, `4x`, `8x`) using both static and skinned dummy meshes to force the DX12/Vulkan driver to finalize hardware PSOs before gameplay starts.
3. **Modern UI Toolkit (`CreateGUI`) PassType Distribution Heatmap (`PsoEliminatorWindow`)**
   - Stacked horizontal `PassType` distribution heatmap bar (`ScriptableRenderPipeline`, `ShadowCaster`, `DepthOnly`, `ForwardBase`, `Meta`), 64-bit FNV-1a driver fingerprint card, interactive search + `PassType` filter buttons, and one-click **Auto-Fix & Deduplicate Manifest**.
4. **MCP (Model Context Protocol) & AI Agent Manifest Optimizer Bridge (`PsoEliminatorMcpBridge`)**
   - Built-in JSON-RPC 2.0 server dispatcher (`HandleJsonRpcRequest`) compatible with **Cursor**, **Claude Desktop**, **Antigravity**, and Unity MCP servers (`protocolVersion: "2024-11-05"`):
     - `pso_eliminator.inspect_manifest`: Returns structured JSON of the active manifest, driver fingerprint, and PassType heatmap.
     - `pso_eliminator.harvest_project_materials`: Harvests project materials into a clean manifest and returns the JSON report.
     - `pso_eliminator.autofix_manifest`: Automatically deduplicates variant records, canonically sorts keywords, removes invalid materials, and sanitizes MSAA sample counts (`1, 2, 4, 8`) and RenderTarget profiles.
     - `pso_eliminator.analyze_runtime_hitches`: Exports detected runtime `PsoHitchEvent` spikes in JSON.
5. **Headless CI / Batchmode Harvesting**
   ```bash
   unity -batchmode -quit -projectPath . -executeMethod YusufOkuducu.PcPsoEliminator.Editor.SceneShaderVariantHarvester.RunCiHarvest
   unity -batchmode -quit -projectPath . -executeMethod YusufOkuducu.PcPsoEliminator.Editor.SceneShaderVariantHarvester.RunMcpAuditJson
   ```

---

## Installation (Unity Package Manager & OpenUPM)

### Via Git URL
Add in **Window -> Package Manager -> + -> Add package from git URL...**:
```text
https://github.com/yusufokuducu/unity-pc-pso-eliminator.git
```

### Via OpenUPM CLI
```bash
openupm add com.yusufokuducu.pc-pso-eliminator
```

---

## Running Standalone Unit Tests (.NET CLI)

```bash
dotnet test DotNetTests~/PcPsoEliminator.Tests.csproj
```

## License

MIT License — see [LICENSE](LICENSE).
