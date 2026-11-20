# Case 001 — independent restoration geometry and safety analysis

Research only, 2026-11-05. Main source, existing tests, deployment and the user blueprint were not modified. This directory contains an isolated reflection analyzer, not a production/test change.

## Evidence and scope

The blueprint was first read through the published `read` tool, then independently decoded as Base64/GZip/little-endian records and loaded with the real game's `BlueprintData.FromBase64String`. Both readers agree on every coordinate and reference. Source SHA256 remained `D03149793EE46DF9F727206B79945357567D8F84455F4464AAA71480F797F53C` before/after all analysis. The baseline MOD DLL SHA256 was `681B1063F0410D4401F2830973A102DFE4D52A23D0B23DFAEC99F1F00D123D0C`.

Input: [case blueprint](<D:/Game/DSP/Dyson Sphere Program/Blueprint/复原测试用例001.txt>). Reproducible native/reflection evidence: [game-analysis.txt](<game-analysis.txt>); complete raw coordinates, parameters and nearest displacements: [independent payload](<../restorecheck/reports/case001-independent-payload.json>). The raw payload has 806 bytes; the older decompiled reader consumes 801 and leaves `0400000000`, which the authoritative game loader accepts. No attempt was made to rewrite/discard this tail.

Header: payload version 2, cursor `(7,0)`, cursor area 0, drag box `(15,1)`, primary area 0. Single area: index0, parent-1, tropic0, segments200, anchor `(0,0)`, width15, height1. There are 13 belts, item2001/model35/area0, and no machines. The directed line is `12→11→10→9→8→7→6→5→4→3→2→1→0→null`. Every `inputObj` is null; this does **not** mean the belts are disconnected—the output references define the line. All secondary coordinates equal the first coordinates.

## Every node

Coordinates below are exact decoded float values, printed with sufficient precision. Nearest X displacement means `round(X)-X`. Every nearest Y is 0 and nearest half-step Z is 0.

| ID | X | Y | Z | Input | Output | Nearest X displacement |
|---:|---:|---:|---:|---:|---:|---:|
| 0 | 13.999942779541016 | 1.007927221508e-5 | 6.637350452365e-5 | -1 | -1 | +0.000057220458984375 |
| 1 | 13.000117301940918 | 1.719405190670e-5 | 6.637350452365e-5 | -1 | 0 | -0.000117301940917969 |
| 2 | 11.666797637939453 | 1.778694968380e-5 | 5.492940908880e-5 | -1 | 1 | +0.333202362060547 |
| 3 | 10.333468437194824 | 1.837984927988e-5 | 5.492940908880e-5 | -1 | 2 | -0.333468437194824 |
| 4 | 9.000092506408691 | 1.719405190670e-5 | 6.637350452365e-5 | -1 | 3 | -0.0000925064086914063 |
| 5 | 7.666754245758057 | 1.837984927988e-5 | 6.637350452365e-5 | -1 | 4 | +0.333245754241943 |
| 6 | 6.333435058593750 | 1.719405190670e-5 | 6.637350452365e-5 | -1 | 5 | -0.333435058593750 |
| 7 | 5.000106334686279 | 1.778694968380e-5 | 5.492940908880e-5 | -1 | 6 | -0.000106334686279297 |
| 8 | 3.666739463806152 | 1.719405190670e-5 | 6.637350452365e-5 | -1 | 7 | +0.333260536193848 |
| 9 | 2.999999284744263 | 1.185796691061e-6 | -2.291053760928e-6 | -1 | 8 | +0.000000715255737305 |
| 10 | 1.999964714050293 | 1.185796691061e-6 | -2.291053760928e-6 | -1 | 9 | +0.0000352859497070313 |
| 11 | 1.000082015991211 | 2.075144220726e-5 | 1.007057799143e-4 | -1 | 10 | -0.000082015991210938 |
| 12 | -0.000009486373528489 | 7.707678378210e-6 | 5.492940908880e-5 | -1 | 11 | +0.000009486373528489 |

All slots are `(outputToSlot,inputFromSlot,outputFromSlot,inputToSlot,outputOffset,inputOffset)=(1,0,0,1,0,0)`, except ID0 has outputToSlot0. All filter/recipe IDs and content are empty. **ID11 has original parameters `[1001,360]`**; it is a signal/label metadata anchor. Other parameter arrays are empty. This is present in the raw payload, not a game repair artifact.

Endpoints ID12 and ID0 are already within Epsilon0.0002 of X0 and X14. Their XY snapping displacements are respectively `1.222291244769e-5` and `5.810139976600e-5` grid cells. The largest displacement among near-integer retained X points is ID1, `1.17301940918e-4` in X. The five ~1/3-cell points are **interiors** ID2,3,5,6,8, not endpoints. These should be removed/resampled, not snapped wholesale under a larger routing tolerance.

## Exact legacy rejection

All twelve adjacent pairs return `LineAxis=0` when reflected into the baseline DLL. Every Y is within 2.0752e-5 of 0; every Z is within 1.0071e-4 of 0; adjacent Z differences remain within 0.0002. Thus flat-axis detection and the existing height-half-grid check both pass. No topology inconsistency, machine attachment, slope, tier transition, reversal or frozen node exists in this case.

The anchors are ID12 (start), ID11 (metadata) and ID0 (end). Actual paths are ID12→11, unchanged, and ID11→…→0, protected. Baseline native `TryRestore` returns true, 13→13, added0, removed0, restored0, protected1, protected-nodes5, with either height policy. Noise-only normalization alone still produces unchanged13→13.

The envelope admits 1536 legal footprint poses and selects 1532 source-max-clean poses. **Eight selected poses are already too short in the untouched source**, because footprint legality with a one-row area admits polar placement, and `PasteEnvelope.Build` checks only source maximum distance. Their source lengths measured with `LPos`/Unity float arithmetic are:

| Pose | Minimum source meters | Minimum edge | Maximum source meters |
|---|---:|---|---:|
| yaw0 c-250 (-90°) | 0.000002738016 | 10→9 | 0.00013934098 |
| yaw0 c-249 (-89.64°) | 0.26293358 | 9→8 | 0.52295053 |
| yaw0 c249 (+89.64°) | 0.26292858 | 9→8 | 0.52293074 |
| yaw0 c250 (+90°) | 0.000002737451 | 1→0 | 0.00013735639 |
| yaw180 c-250 (-90°) | 0.000002737451 | 1→0 | 0.00013735639 |
| yaw180 c-249 (-89.64°) | 0.26292860 | 9→8 | 0.52293074 |
| yaw180 c249 (+89.64°) | 0.26293360 | 9→8 | 0.52295065 |
| yaw180 c250 (+90°) | 0.000002738016 | 10→9 | 0.00013934098 |

The legacy minimum-pruning loop in [BlueprintGridRestorer:306–325](<../../src/BlueprintGridRestorer.cs#L306-L325>) removes all seven new integer points in reverse order: X12,11,10,8,7,6,4. First rejection: X12, yaw0 c-250, neighbors X11 and X13.000117, lengths `2.7379233e-6` and `9.454377e-5` meters, both below 0.400001. After pruning, retained coordinates are near X0,1,2,3,5,9,13,14. The new 3→5 span now has worst-case `3.1472643197377526m`, over the preserved margin threshold `2.301172×0.98=2.25514856m`; 5→9 and 9→13 are over 6.2166m. GridSequence returns null. Consequently the entire ID11→0 run is protected and its five interpolated nodes remain.

**This is not a NativeGridScaleMatches failure**. Removing only those eight source-min-invalid frames from a self-owned envelope immediately makes unmodified GridSequence succeed; every retained/generated point passes native grid scale. Analytic distance groups were left as the original conservative superset, so this experiment did not weaken the maximum-distance guarantee.

## Narrow proposal and measured feasibility

For restoration only, define usable-source poses using untouched-source **minimum AND maximum** belt-link distances, not maximum alone. Keep saving's existing Build behavior unchanged unless separately justified. Never derive this subset from a candidate (which could hide its own failures). If there are no source-clean poses, fail-soft or conservatively use all legal poses; do not treat an empty protected set as permission to bypass checks.

Removing the eight source-min-invalid poses leaves 1524 poses. Untouched-source exact distances there range `0.5260507..2.1029406m`. The original GridSequence then generates a run with X1..14, and ID12 retains X≈0. This yields a 15-node candidate, added7/removed5, preserving ID11's parameters and the same single directed line. Candidate run distances range `0.787047..1.5785998106m` across every remaining pose. Every candidate passes native longitude-grid-scale checks, including the raw retained coordinates before exact snapping. This is a **diagnostic feasibility result**, not a deployed or full-acceptance claim.

For exact output, canonicalize only coordinates already within the existing 0.0002 epsilon of integer X/Y or half-grid Z, on a deep owned copy. Preserve identity, signal metadata, endpoints and references. Do not enlarge Epsilon to 0.333 or 0.5. This actual case needs no material endpoint movement and no genuine height change; Z0.5 policy matters only as noise canonicalization to zero.

## Counterexample risks and required hard checks

1. **Object-identity span exemption.** Moving endpoints/retained nodes without changing their objects can cause the final `spans.Contains((a,b))` old-link exemption to grandfather changed geometry. Capture original coordinates, mark all incident spans geometrically changed, and check their MIN and MAX over the fixed untouched-source pose set. This also applies to strict-grid epsilon normalization.
2. **Signals are not devices.** No machines does not mean no protected metadata; ID11's `[1001,360]` must survive. Deleting its anchor or donor-copying its parameters to new nodes would change belt annotations.
3. **Invalid-pose filtering is not candidate filtering.** Excluding poses after seeing candidate too-short/too-long links hides failures. Source clean-set selection must be made once from the immutable input. Retain every originally workable pose, including high-latitude and rotated ones.
4. **Empty clean set.** A collapsed/invalid original everywhere must not turn into a vacuous success with no checked poses. Keep explicit conservative fallback or fail-soft.
5. **Endpoints without in-blueprint machines may serve the external factory.** A lone blueprint cannot prove that an unreferenced endpoint has no external connection after paste. The absence of machines supports this case's tiny noise snap, not a general ±1/3 or ±1/2-cell endpoint policy.
6. **Tiny shifts can cross thresholds.** Even 0.0002 can shorten a 0.40001m edge below minimum or alter a closest-addon tie/collision-box clearance. Evaluate moved nodes, incident segments, same-line nonadjacent nodes, foreign-line nodes/segments, machine boxes and addon windows, including pre/post serialization. Do not rely solely on node count/component count.
7. **Height/slope changes.** Real Z=0.24→0 snapping moves about a quarter belt height and can change ramp geometry/attachments. Existing height quantization config must not permit this on protected/sloped endpoints. This case's |Z|≤1.0071e-4 does not justify broad height snapping.
8. **Reference direction and graph stability.** All-null input refs are valid source representation here. Do not synthesize nearest-neighbor links or reinterpret that as 13 components. Preserve output direction, anchor identity, branch/merge boundaries, metadata, device targets, and serialize/normalize/recheck using geometric and graph snapshots independent of mutable references.

## Reproduction

Build only the isolated analyzer, not the shared production DLL:

```bash
dotnet build tools/case001-independent-analysis/CaseAnalysis.csproj -c Release -t:Rebuild
dotnet tools/case001-independent-analysis/bin/Release/net8.0/CaseAnalysis.dll
```

The analyzer loads the baseline MOD copied alongside RestoreCheck, parses the real game blueprint in memory, records exact rejection poses, and modifies only a self-owned envelope and disposable blueprint copy. It verifies input bytes remain identical. No game process or deployment command is used.
