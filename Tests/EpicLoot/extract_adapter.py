"""Extract the production adapter verbatim for isolated source-linked tests."""
from pathlib import Path
import re
import sys

source = Path(sys.argv[1]).read_text(encoding="utf-8-sig")
marker = "internal static class EpicLootBagCompatibility"
start = source.index(marker)
opening = source.index("{", start)
depth = 0
end = None
for pos in range(opening, len(source)):
    if source[pos] == "{":
        depth += 1
    elif source[pos] == "}":
        depth -= 1
        if depth == 0:
            end = pos + 1
            break
if end is None:
    raise RuntimeError("Production adapter closing brace was not found")
imports = "\n".join(re.findall(r"^using [^\n]+;\s*$", source[:start], re.M))
destination = Path(sys.argv[2])
destination.parent.mkdir(parents=True, exist_ok=True)
destination.write_text("#nullable enable\n" + imports + "\nnamespace TrollingFishing;\n" + source[start:end] + "\n", encoding="utf-8")
