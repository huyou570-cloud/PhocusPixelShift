# Mac 0.2 validation / 验证记录

2026-10-17. Installed engine: pinned Phocus 4.1.1 universal components. Version labels are diagnostic, engine ABI/hash checks remain required.

| Path | Samples | TIFF output | Result |
| --- | --- | --- | --- |
| x86_64 under Rosetta | 4 | 11656 × 8742, RGB16 | Passed; exit 0 |
| x86_64 under Rosetta | 6 | 23312 × 17484, RGB16 | Passed; exit 0 |
| arm64 native regression | 4 | 11656 × 8742, RGB16 | Passed; exit 0 |

All merged RAW samples survived native serialization/reopening unchanged; tested native color crops matched their reopened-file path. Full TIFF headers were independently checked. Source hashes were unchanged. Temporary FFF/TIFF images were deleted; local logs remain outside this repository.

Universal UI contains arm64 and x86_64 slices; the x86_64 UI was launched under Rosetta. Local ad-hoc signature verified. 33 offline workflow tests and skill validation passed.

Intel 4/6-frame tests ran under Rosetta on Apple Silicon. Physical Intel hardware and Intel 16-frame processing are not yet validated. Cross-architecture pixel identity and Phocus GUI export color equivalence are not established. No optical resolution comparison is claimed.

Intel 适配已通过 Rosetta 下的 4／6 张真实样片验收；物理 Intel Mac 和 Intel 16 张尚未验收。测试成片已清理，原片未改动。
