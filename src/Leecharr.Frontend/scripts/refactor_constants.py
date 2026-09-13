import re
import json
import random
import string

with open("src/pages/AutomationPage.tsx", "r") as f:
    content = f.read()

translations = {}
with open("extracted_strings.json", "r") as f:
    try:
        translations = json.load(f)
    except:
        pass

def add_translation(text):
    text = text.strip()
    if not text:
        return ""
    key = re.sub(r'[^a-zA-Z0-9\s]', '', text).strip().split()[:5]
    if not key:
        key_str = "str" + ''.join(random.choices(string.ascii_lowercase, k=7))
    else:
        key_str = key[0].lower() + ''.join(w.capitalize() for w in key[1:])
    
    final_key = key_str
    counter = 1
    while final_key in translations and translations[final_key] != text:
        final_key = key_str + str(counter)
        counter += 1
    translations[final_key] = text
    return final_key

# We will regex replace things like `label: "..."` with `label: t("...")`
def replacer(match):
    prefix = match.group(1)
    text = match.group(2)
    key = add_translation(text)
    return f'{prefix}t("automation.{key}")'

# Regex for common properties: label, group, desc, extraHelp
for prop in ["label", "group", "desc", "extraHelp"]:
    pattern = rf'({prop}:\s*)"([^"\\]*(?:\\.[^"\\]*)*)"'
    content = re.sub(pattern, replacer, content)

# For TRIGGER_LABELS, it's like:
# TorrentAdded: "📥 On Torrent Added",
# We can match `Word: "..."` inside the TRIGGER_LABELS block.
# Actually, it's easier to just match `(\w+:\s*)"([^"\\]*(?:\\.[^"\\]*)*)"`
# but only inside the TRIGGER_LABELS dict.
trigger_labels_match = re.search(r'const TRIGGER_LABELS: Record<string, string> = \{([\s\S]*?)\};', content)
if trigger_labels_match:
    block = trigger_labels_match.group(1)
    def trigger_replacer(m):
        prefix = m.group(1)
        text = m.group(2)
        key = add_translation(text)
        return f'{prefix}t("automation.{key}")'
    new_block = re.sub(r'(\w+:\s*)"([^"\\]*(?:\\.[^"\\]*)*)"', trigger_replacer, block)
    content = content.replace(block, new_block)

with open("src/pages/AutomationPage.tsx", "w") as f:
    f.write(content)

with open("extracted_strings.json", "w") as f:
    json.dump(translations, f, indent=2)

print("Constants extracted.")
