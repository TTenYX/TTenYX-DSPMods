#!/usr/bin/env bash
# Reproducible saving regression in a NEW directory; do not overwrite suite-v8 evidence.
set -euo pipefail
R="X:/戴森球计划/【MOD】修改仓库"
cd "$R"
S="$R/1.我的MOD/BlueprintBeltSaver/tools/restorecheck/reports/saving-regression"
C="$R/.work/belt-regression/bin/Release/net8.0/Checks.dll"
P="$R/1.我的MOD/BlueprintBeltSaver/tools/abprobe/bin/Release/net8.0/TopoProbe.dll"
T="$R/7.蓝图Blueprints/BlueprintBeltSaver 测试蓝图"
dotnet build .work/belt-regression/Checks.csproj -c Release -t:Rebuild > "$R/.work/restore-build-checks.log"
dotnet build 1.我的MOD/BlueprintBeltSaver/tools/abprobe/TopoProbe.csproj -c Release -t:Rebuild > "$R/.work/restore-build-probe.log"
for cap in 2.301172 1.9; do
  mkdir -p "$S/cap$cap"
  cp .work/belt-regression/suite-v8-全姿态包络/cap2.301172/matrix-list.txt "$S/cap$cap/matrix-list.txt"
  dotnet "$C" --matrix "$S/cap$cap/matrix-list.txt" "$cap" > "$S/cap$cap/matrix.log"
  dotnet "$C" --flow "$S/cap$cap/matrix-list.txt" "$cap" > "$S/cap$cap/flow.log"
done
mkdir -p "$S/A"
dotnet "$P" --export "$T" "$S/A" > "$S/export.log"
dotnet "$P" --pasteaudit "$S/A" "$T" --out "$S/pasteaudit.md" > "$S/pasteaudit.log"
dotnet "$P" --addons "$S/A" "$T" > "$S/addons.log"
dotnet build .work/bb-saver-check/BbSaverCheck.csproj -c Release -t:Rebuild > "$R/.work/restore-build-il.log"
dotnet run --project .work/bb-saver-check/BbSaverCheck.csproj -c Release --no-build > "$S/il.log"
# Check outcomes even for legacy CLIs that historically returned exit 0 on assertion failures.
python - "$S" <<'PY'
import sys
from pathlib import Path
s=Path(sys.argv[1])
checks={'cap2.301172/matrix.log':'MATRIX ALL PASS','cap1.9/matrix.log':'MATRIX ALL PASS','cap2.301172/flow.log':'FLOW ALL PASS','cap1.9/flow.log':'FLOW ALL PASS','pasteaudit.log':'PASTE AUDIT ALL PASS','addons.log':'ADDON CENSUS ALL KEPT','il.log':'ALL CHECKS PASSED'}
for name,expected in checks.items():
    b=(s/name).read_bytes()
    t=b.decode('utf-8','replace')
    if expected not in t: raise SystemExit('FAIL '+name)
    print('PASS '+name+' '+expected)
if '| FAIL' in (s/'A/判定报告.md').read_text(encoding='utf-8'): raise SystemExit('FAIL export')
print('SAVING REGRESSION ALL PASS')
PY
