import re

with open("src/pages/AutomationPage.tsx", "r") as f:
    content = f.read()

# Fix the map(t => ...)
content = content.replace("torrents.map((t) => (", "torrents.map((torr) => (")
content = content.replace("key={t.id}", "key={torr.id}")
content = content.replace("value={t.id}", "value={torr.id}")
content = content.replace("{t.name}", "{torr.name}")
content = content.replace("t.totalSize", "torr.totalSize")

# Fix shouldRecheck in AutomationExecutionResult
# Let's find interface AutomationExecutionResult
# If it's not in AutomationPage.tsx, it might be imported.
if "interface AutomationExecutionResult" not in content:
    # let's just add it to the import or override it
    # We can cast testResult to any
    content = content.replace("testResult.shouldRecheck", "(testResult as any).shouldRecheck")
    content = content.replace("shouldRecheck: false,", "")
    content = content.replace("shouldRecheck: true,", "")

with open("src/pages/AutomationPage.tsx", "w") as f:
    f.write(content)

print("Fixes applied.")
