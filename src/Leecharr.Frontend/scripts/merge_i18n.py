import json
import re

with open("extracted_strings.json", "r") as f:
    translations = json.load(f)

# The en.ts file looks like:
# const en: I18nTranslations = {
#   "components": { ... },
#   ...
# }

with open("src/i18n/locales/en.ts", "r") as f:
    content = f.read()

# I will find `const en: I18nTranslations = {` and insert `"automation": { ... },` right after it
automation_json = json.dumps(translations, indent=4)
# add indent to automation_json
automation_json = "\n".join("  " + line for line in automation_json.split("\n"))

insert_idx = content.find("const en: I18nTranslations = {")
if insert_idx == -1:
    insert_idx = content.find("const en = {")
insert_idx = content.find("{", insert_idx) + 1

new_content = content[:insert_idx] + '\n  "automation": ' + automation_json.strip() + "," + content[insert_idx:]

with open("src/i18n/locales/en.ts", "w") as f:
    f.write(new_content)

print("Merged automation strings to en.ts")
