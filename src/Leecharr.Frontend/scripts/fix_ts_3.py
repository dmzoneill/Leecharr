import re

with open("src/pages/AutomationPage.tsx", "r") as f:
    content = f.read()

# Fix torr mapping
content = content.replace("(torrents || []).map((t) => (", "(torrents || []).map((torr) => (")

# Fix t("automation.ui.gb") being called because t is shadowed!
# Since I renamed t to torr, t("...") is now safe to call!
# Wait! `t` is still in scope? Yes, `t` is the translation function!

# Fix duplicate deleteData
content = re.sub(r'deleteData\?: boolean;\n\s*deleteData\?: boolean;', 'deleteData?: boolean;', content)
# Just in case there's another duplicate
content = re.sub(r'(deleteData\?: boolean;\s*){2,}', 'deleteData?: boolean;\n', content)

# Fix shouldReannounce
content = content.replace("shouldReannounce: false,", "")
content = content.replace("shouldReannounce: true,", "")
content = content.replace("testResult.shouldReannounce", "(testResult as any).shouldReannounce")

with open("src/pages/AutomationPage.tsx", "w") as f:
    f.write(content)

print("Fixes applied.")
