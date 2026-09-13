const fs = require('fs');
let content = fs.readFileSync('/home/daoneill/src/usr/leecharr/src/Leecharr.Frontend/src/pages/AutomationPage.tsx', 'utf8');

const targetStr = `                                ) : act.type === "pause" || act.type === "resume" || act.type === "recheck" || act.type === "reannounce" || act.type === "boostTracker" ? (
                                  <span style={{ flex: 1, fontSize: "0.85rem", color: "var(--text-muted)", paddingLeft: "0.25rem" }}>
                                    ✨ Auto-applies to active swarm & torrent
                                  </span>
                                ) : (
                                  <input
                                    type={act.type === "setUploadLimit" || act.type === "setDownloadLimit" || act.type === "setRatioLimit" || act.type === "setSeedingTimeLimit" || act.type === "delay" ? "number" : "text"}
                                    className="form-control"
                                    style={{ flex: 1, minWidth: "180px" }}
                                    value={act.value}
                                    onChange={(e) => {
                                      const copy = [...visualSteps];
                                      copy[stepIdx].actions[actIdx].value = e.target.value;
                                      updateVisualSteps(copy);
                                    }}
                                    placeholder={placeholder}
                                  />
                                )}`;

const replacementStr = `                                ) : act.type === "pause" || act.type === "resume" || act.type === "recheck" || act.type === "reannounce" || act.type === "boostTracker" || act.type === "calculateChecksum" || act.type === "reannounceAll" || act.type === "exportTorrent" ? (
                                  <span style={{ flex: 1, fontSize: "0.85rem", color: "var(--text-muted)", paddingLeft: "0.25rem" }}>
                                    ✨ Auto-applies to active swarm & torrent
                                  </span>
                                ) : act.type === "setShareLimitAction" ? (
                                  <select
                                    className="form-control"
                                    style={{ flex: 1, minWidth: "180px" }}
                                    value={act.value || "Pause"}
                                    onChange={(e) => {
                                      const copy = [...visualSteps];
                                      copy[stepIdx].actions[actIdx].value = e.target.value;
                                      updateVisualSteps(copy);
                                    }}
                                  >
                                    <option value="Pause">⏸️ Pause Torrent</option>
                                    <option value="Remove">🗑️ Remove Torrent</option>
                                  </select>
                                ) : act.type === "setFilePermissions" ? (
                                  <select
                                    className="form-control"
                                    style={{ flex: 1, minWidth: "180px" }}
                                    value={act.value || "0777"}
                                    onChange={(e) => {
                                      const copy = [...visualSteps];
                                      copy[stepIdx].actions[actIdx].value = e.target.value;
                                      updateVisualSteps(copy);
                                    }}
                                  >
                                    <option value="0777">0777 (Read/Write/Execute All)</option>
                                    <option value="0755">0755 (Read/Execute All, Write Owner)</option>
                                    <option value="0644">0644 (Read All, Write Owner)</option>
                                  </select>
                                ) : act.type === "sendDiscordWebhook" ? (
                                  <div style={{ flex: 1, display: "flex", gap: "0.5rem", minWidth: "180px" }}>
                                    <input
                                      type="text"
                                      className="form-control"
                                      style={{ flex: 2 }}
                                      placeholder="Webhook URL"
                                      value={act.value?.split('|')[0] || ""}
                                      onChange={(e) => {
                                        const copy = [...visualSteps];
                                        const parts = (copy[stepIdx].actions[actIdx].value || "").split('|');
                                        copy[stepIdx].actions[actIdx].value = \`\${e.target.value}|\${parts[1] || ""}|\${parts[2] || ""}\`;
                                        updateVisualSteps(copy);
                                      }}
                                    />
                                    <input
                                      type="color"
                                      className="form-control"
                                      style={{ width: "40px", padding: "0.1rem" }}
                                      value={act.value?.split('|')[1] || "#000000"}
                                      onChange={(e) => {
                                        const copy = [...visualSteps];
                                        const parts = (copy[stepIdx].actions[actIdx].value || "").split('|');
                                        copy[stepIdx].actions[actIdx].value = \`\${parts[0] || ""}|\${e.target.value}|\${parts[2] || ""}\`;
                                        updateVisualSteps(copy);
                                      }}
                                    />
                                    <input
                                      type="text"
                                      className="form-control"
                                      style={{ flex: 1 }}
                                      placeholder="Embed Title"
                                      value={act.value?.split('|')[2] || ""}
                                      onChange={(e) => {
                                        const copy = [...visualSteps];
                                        const parts = (copy[stepIdx].actions[actIdx].value || "").split('|');
                                        copy[stepIdx].actions[actIdx].value = \`\${parts[0] || ""}|\${parts[1] || ""}|\${e.target.value}\`;
                                        updateVisualSteps(copy);
                                      }}
                                    />
                                  </div>
                                ) : act.type === "sendNtfy" ? (
                                  <div style={{ flex: 1, display: "flex", gap: "0.5rem", minWidth: "180px" }}>
                                    <input
                                      type="text"
                                      className="form-control"
                                      style={{ flex: 2 }}
                                      placeholder="Topic URL"
                                      value={act.value?.split('|')[0] || ""}
                                      onChange={(e) => {
                                        const copy = [...visualSteps];
                                        const parts = (copy[stepIdx].actions[actIdx].value || "").split('|');
                                        copy[stepIdx].actions[actIdx].value = \`\${e.target.value}|\${parts[1] || "default"}\`;
                                        updateVisualSteps(copy);
                                      }}
                                    />
                                    <select
                                      className="form-control"
                                      style={{ flex: 1 }}
                                      value={act.value?.split('|')[1] || "default"}
                                      onChange={(e) => {
                                        const copy = [...visualSteps];
                                        const parts = (copy[stepIdx].actions[actIdx].value || "").split('|');
                                        copy[stepIdx].actions[actIdx].value = \`\${parts[0] || ""}|\${e.target.value}\`;
                                        updateVisualSteps(copy);
                                      }}
                                    >
                                      <option value="min">Min Priority</option>
                                      <option value="low">Low Priority</option>
                                      <option value="default">Default Priority</option>
                                      <option value="high">High Priority</option>
                                      <option value="max">Max Priority</option>
                                    </select>
                                  </div>
                                ) : act.type === "evalMath" ? (
                                  <div style={{ flex: 1, display: "flex", gap: "0.5rem", minWidth: "180px" }}>
                                    <input
                                      type="text"
                                      className="form-control"
                                      style={{ flex: 2 }}
                                      placeholder="Formula"
                                      value={act.value?.split('|')[0] || ""}
                                      onChange={(e) => {
                                        const copy = [...visualSteps];
                                        const parts = (copy[stepIdx].actions[actIdx].value || "").split('|');
                                        copy[stepIdx].actions[actIdx].value = \`\${e.target.value}|\${parts[1] || ""}\`;
                                        updateVisualSteps(copy);
                                      }}
                                    />
                                    <input
                                      type="text"
                                      className="form-control"
                                      style={{ flex: 1 }}
                                      placeholder="Target Var"
                                      value={act.value?.split('|')[1] || ""}
                                      onChange={(e) => {
                                        const copy = [...visualSteps];
                                        const parts = (copy[stepIdx].actions[actIdx].value || "").split('|');
                                        copy[stepIdx].actions[actIdx].value = \`\${parts[0] || ""}|\${e.target.value}\`;
                                        updateVisualSteps(copy);
                                      }}
                                    />
                                  </div>
                                ) : (
                                  <input
                                    type={act.type === "setUploadLimit" || act.type === "setDownloadLimit" || act.type === "setRatioLimit" || act.type === "setSeedingTimeLimit" || act.type === "delay" || act.type === "retryStep" ? "number" : "text"}
                                    className="form-control"
                                    style={{ flex: 1, minWidth: "180px" }}
                                    value={act.value}
                                    onChange={(e) => {
                                      const copy = [...visualSteps];
                                      copy[stepIdx].actions[actIdx].value = e.target.value;
                                      updateVisualSteps(copy);
                                    }}
                                    placeholder={placeholder}
                                  />
                                )}`;

if (!content.includes(targetStr)) {
  console.log("Could not find target string.");
  process.exit(1);
}

content = content.replace(targetStr, replacementStr);
fs.writeFileSync('/home/daoneill/src/usr/leecharr/src/Leecharr.Frontend/src/pages/AutomationPage.tsx', content, 'utf8');
console.log("Success");
