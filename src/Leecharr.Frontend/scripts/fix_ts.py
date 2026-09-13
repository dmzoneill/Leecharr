import re

with open("src/pages/AutomationPage.tsx", "r") as f:
    content = f.read()

# Fix 1: VisualAction needs deleteData
content = content.replace("extra?: Record<string, any>;", "extra?: Record<string, any>;\n  deleteData?: boolean;")

# Fix 2: cleanUnwantedFiles string literal error
content = content.replace('type: "cleanUnwantedFiles"', 'type: "cleanFiles"')

# Fix 3: Parameter 'valStr' implicitly has an 'any' type.
content = content.replace("const resolveVal = (valStr) => {", "const resolveVal = (valStr: any) => {")

# Fix 4: Parameter 'val' implicitly has an 'any' type.
content = content.replace("const updateAct = (val) => {", "const updateAct = (val: any) => {")
content = content.replace("const updateExtra = (key, val) => {", "const updateExtra = (key: any, val: any) => {")

# Fix 7: Element implicitly has an 'any' type ... (labels)
# Change `const labels = { 0: ... }` to `const labels: any = { 0: ... }`
content = content.replace("const labels = {", "const labels: any = {")

# Fix 8: "sleep" comparison
content = content.replace('|| act.type === "sleep"', '')

# Fix 9: methodColors
content = content.replace("methodColors[m]", "methodColors[m as keyof typeof methodColors]")

# Fix 10: torrent -> sampleTorrent
content = content.replace("${torrent.name}", "${sampleTorrent?.name}")
content = content.replace("${torrent.size}", "${sampleTorrent?.size}")
content = content.replace('onClick={() => updateAct(`${m}|${url}|{ "event": "TorrentCompleted", "name": "${sampleTorrent?.name}", "size": ${sampleTorrent?.size} }`)}',
                          'onClick={() => updateAct(`${m}|${url}|{ "event": "TorrentCompleted", "name": "sample", "size": 0 }`)}')


# Fix 11: shouldRecheck
# Property 'shouldRecheck' does not exist on type 'AutomationExecutionResult'.
# Let's add it to AutomationExecutionResult interface
content = content.replace("stepsExecuted: number;", "stepsExecuted: number;\n  shouldRecheck?: boolean;")

with open("src/pages/AutomationPage.tsx", "w") as f:
    f.write(content)

print("TS errors fixed.")
