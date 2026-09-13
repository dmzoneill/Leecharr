const fs = require('fs');
let content = fs.readFileSync('/home/daoneill/src/usr/leecharr/src/Leecharr.Frontend/src/pages/AutomationPage.tsx', 'utf8');

const targetStr = `                                <button
                                  type="button"
                                  className="btn btn-sm btn-secondary"
                                  style={{ width: "32px", height: "32px", padding: 0, display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0, borderRadius: "6px" }}
                                  title="Delete Action"
                                  onClick={() => {
                                    const copy = [...visualSteps];
                                    copy[stepIdx].actions = copy[stepIdx].actions.filter((_, idx) => idx !== actIdx);
                                    updateVisualSteps(copy);
                                  }}
                                >
                                  ✕
                                </button>`;

const replacementStr = `                                <button
                                  type="button"
                                  className="btn btn-sm btn-secondary"
                                  disabled={actIdx === 0}
                                  style={{ width: "32px", height: "32px", padding: 0, display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0, borderRadius: "6px" }}
                                  title="Move Up"
                                  onClick={() => {
                                    const copy = [...visualSteps];
                                    const temp = copy[stepIdx].actions[actIdx];
                                    copy[stepIdx].actions[actIdx] = copy[stepIdx].actions[actIdx - 1];
                                    copy[stepIdx].actions[actIdx - 1] = temp;
                                    updateVisualSteps(copy);
                                  }}
                                >
                                  ⬆️
                                </button>
                                <button
                                  type="button"
                                  className="btn btn-sm btn-secondary"
                                  disabled={actIdx === step.actions.length - 1}
                                  style={{ width: "32px", height: "32px", padding: 0, display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0, borderRadius: "6px" }}
                                  title="Move Down"
                                  onClick={() => {
                                    const copy = [...visualSteps];
                                    const temp = copy[stepIdx].actions[actIdx];
                                    copy[stepIdx].actions[actIdx] = copy[stepIdx].actions[actIdx + 1];
                                    copy[stepIdx].actions[actIdx + 1] = temp;
                                    updateVisualSteps(copy);
                                  }}
                                >
                                  ⬇️
                                </button>
                                <button
                                  type="button"
                                  className="btn btn-sm btn-secondary"
                                  style={{ width: "32px", height: "32px", padding: 0, display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0, borderRadius: "6px" }}
                                  title="Duplicate Action"
                                  onClick={() => {
                                    const copy = [...visualSteps];
                                    const cloned = { ...copy[stepIdx].actions[actIdx], id: \`act-\${Date.now()}-\${Math.random()}\` };
                                    copy[stepIdx].actions.splice(actIdx + 1, 0, cloned);
                                    updateVisualSteps(copy);
                                  }}
                                >
                                  📋
                                </button>
                                <button
                                  type="button"
                                  className="btn btn-sm btn-secondary"
                                  style={{ width: "32px", height: "32px", padding: 0, display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0, borderRadius: "6px" }}
                                  title="Delete Action"
                                  onClick={() => {
                                    const copy = [...visualSteps];
                                    copy[stepIdx].actions = copy[stepIdx].actions.filter((_, idx) => idx !== actIdx);
                                    updateVisualSteps(copy);
                                  }}
                                >
                                  ✕
                                </button>`;

if (!content.includes(targetStr)) {
  console.log("Could not find target string.");
  process.exit(1);
}

content = content.replace(targetStr, replacementStr);
fs.writeFileSync('/home/daoneill/src/usr/leecharr/src/Leecharr.Frontend/src/pages/AutomationPage.tsx', content, 'utf8');
console.log("Success");
