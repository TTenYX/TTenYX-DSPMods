# -*- coding: utf-8 -*-
"""
A/B 字节级对比（004.16 重建版；原 _abdiff.py 在 2026-11-01 目录迁移中遗失，本文件为其替代）。

用法：python _abdiff.py <A目录> <B目录> [输出.md]
逻辑：按文件名配对（A_lib_X.txt ↔ B_lib_X.txt、A_lat<L>_X.txt ↔ B_lat<L>_X.txt），
逐字节比较，输出 一致/差异 + 首差异偏移。
注意：字节不一致是常态（B 侧多一次序列化往返，浮点重编码）——字节级只做粗筛，
最终裁决一律用 TopoProbe --semdiff（语义多重集）。
"""
import os
import sys


def main():
    if len(sys.argv) < 3:
        print("用法：python _abdiff.py <A目录> <B目录> [输出.md]")
        return 2
    a_dir, b_dir = sys.argv[1], sys.argv[2]
    out_md = sys.argv[3] if len(sys.argv) > 3 else None

    def role(name):
        # A_lib_X / A_lat<L>_X → (键, 侧)
        if name.startswith("A_lib_"):
            return ("lib_" + name[len("A_lib_"):], "A")
        if name.startswith("A_lat"):
            rest = name[len("A_lat"):]
            sep = rest.find("_")
            if sep > 0:
                return ("lat" + rest[:sep] + "_" + rest[sep + 1:], "A")
        if name.startswith("B_lib_"):
            return ("lib_" + name[len("B_lib_"):], "B")
        if name.startswith("B_lat"):
            rest = name[len("B_lat"):]
            sep = rest.find("_")
            if sep > 0:
                return ("lat" + rest[:sep] + "_" + rest[sep + 1:], "B")
        return (None, None)

    a_files = {}
    for f in os.listdir(a_dir):
        key, side = role(f)
        if key and side == "A":
            a_files[key] = os.path.join(a_dir, f)
    rows = []
    same = diff = missing = 0
    for f in sorted(os.listdir(b_dir)):
        key, side = role(f)
        if not key or side != "B" or key not in a_files:
            continue
        a_data = open(a_files[key], "rb").read()
        b_data = open(os.path.join(b_dir, f), "rb").read()
        if a_data == b_data:
            same += 1
            rows.append("| %s | 一致 | — |" % key)
        else:
            diff += 1
            i = next((k for k in range(min(len(a_data), len(b_data))) if a_data[k] != b_data[k]),
                     min(len(a_data), len(b_data)))
            rows.append("| %s | 不同 | 首差异偏移 %d（A %dB / B %dB） |" % (key, i, len(a_data), len(b_data)))
    missing = len(a_files) - (same + diff)
    print("配对 %d：一致 %d，不同 %d，A 有 B 无 %d" % (same + diff, same, diff, max(0, missing)))
    for r in rows:
        print("  " + r)
    if out_md:
        with open(out_md, "w", encoding="utf-8") as h:
            h.write("| 配对键 | 字节级 | 备注 |\n|---|---|---|\n" + "\n".join(rows) + "\n")
            h.write("\n> 字节不一致是常态（B 侧多一次序列化往返）；最终裁决用 TopoProbe --semdiff。\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
