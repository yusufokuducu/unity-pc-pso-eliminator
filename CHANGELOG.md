# Changelog

All notable changes to `com.yusufokuducu.pc-pso-eliminator` are documented in this file.

## [1.2.0] - 2026-11-18

### Added
- **Modern UI Toolkit (`CreateGUI`) PassType Distribution Heatmap Dashboard**:
  - Upgraded `PsoEliminatorWindow` with `UnityEngine.UIElements` (`VisualElement`, stacked horizontal `PassType` distribution heatmap bar, 64-bit FNV-1a driver & manifest fingerprint card, interactive search + `PassType` filter buttons, and one-click `Auto-Fix & Deduplicate Manifest`) while preserving `OnGUI` fallback compatibility.
- **MCP (Model Context Protocol) & AI Agent JSON-RPC 2.0 Bridge (`PsoEliminatorMcpBridge`)**:
  - Full JSON-RPC 2.0 server dispatcher (`HandleJsonRpcRequest`) supporting `initialize`, `tools/list`, and `tools/call` for AI coding assistants (Cursor, Claude, Antigravity).
  - Implemented `pso_eliminator.inspect_manifest`, `pso_eliminator.harvest_project_materials`, `pso_eliminator.autofix_manifest`, and `pso_eliminator.analyze_runtime_hitches`.
  - Added automated manifest optimizer (`AutoFixAndOptimizeManifest`) that deduplicates variant records, canonically sorts keywords, prunes invalid/unsupported materials, and normalizes MSAA sample counts (`1, 2, 4, 8`) and RenderTarget profiles.
  - Added `SceneShaderVariantHarvester.RunMcpAuditJson()` batchmode entrypoint exporting `Logs/PsoWarmupMcpReport.json`.
- **GitHub Actions CI & OpenUPM Metadata**:
  - Added `.github/workflows/ci.yml` running the standalone `.NET 8` NUnit test suite on every push and pull request.
  - Added OpenUPM-compliant metadata (`documentationUrl`, `changelogUrl`, `licensesUrl`) to `package.json`.

## [1.1.0] - 2026-11-18

### Added
- Multi-MSAA (`1x`, `2x`, `4x`, `8x`) and skinned-mesh (`BoneWeight`) offscreen draw-state binding in `AsyncPsoWarmupOrchestrator` so DX12/Vulkan drivers compile both static and skinned hardware PSOs across MSAA targets.
- Incorporated `renderTargetProfiles` and `msaaSampleCounts` into `ComputeDriverAndManifestFingerprint`.

## [1.0.0] - 2026-11-18

### Added
- Initial release of `AsyncPsoWarmupOrchestrator`, `PsoWarmupManifest`, `SceneShaderVariantHarvester`, and `PsoEliminatorWindow`.
