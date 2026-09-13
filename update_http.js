const fs = require('fs');
let content = fs.readFileSync('/home/daoneill/src/usr/leecharr/src/Leecharr.Frontend/src/pages/AutomationPage.tsx', 'utf8');

const replacementStr = `                                  if (act.type === "http") {
                                    const methodColors = { GET: "#3b82f6", POST: "#22c55e", PUT: "#f59e0b", DELETE: "#ef4444", PATCH: "#a855f7" };
                                    const m = val.split('|')[0] || "GET";
                                    const url = val.split('|')[1] || "";
                                    const body = val.split('|')[2] || "";
                                    const chips = ["\${torrent.hash}", "\${torrent.name}", "\${torrent.category}", "\${torrent.size}"];
                                    
                                    return (
                                      <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.5rem", minWidth: "220px", borderLeft: \`3px solid \${methodColors[m] || "#888"}\`, paddingLeft: "0.5rem" }}>
                                        <div style={{ display: "flex", gap: "0.5rem" }}>
                                          <select className="form-control" style={{ width: "90px", fontWeight: 700, color: methodColors[m] }} value={m} onChange={(e) => updateAct(\`\${e.target.value}|\${url}|\${body}\`)}>
                                            <option value="GET">GET</option>
                                            <option value="POST">POST</option>
                                            <option value="PUT">PUT</option>
                                            <option value="PATCH">PATCH</option>
                                            <option value="DELETE">DELETE</option>
                                          </select>
                                          <input type="text" className="form-control" style={{ flex: 1 }} placeholder="https://api.example.com/webhook" value={url} onChange={(e) => updateAct(\`\${m}|\${e.target.value}|\${body}\`)} />
                                        </div>
                                        
                                        <div style={{ display: "flex", gap: "0.3rem", flexWrap: "wrap", alignItems: "center" }}>
                                          <span style={{ fontSize: "0.7rem", color: "var(--text-muted)", marginRight: "0.2rem" }}>Insert:</span>
                                          {chips.map(c => <button key={c} type="button" className="btn btn-secondary" style={{ fontSize: "0.7rem", padding: "0.15rem 0.45rem" }} onClick={() => updateAct(\`\${m}|\${url + c}|\${body}\`)}>{c}</button>)}
                                        </div>

                                        {(m !== "GET" && m !== "DELETE") && (
                                          <div style={{ display: "flex", flexDirection: "column", gap: "0.3rem" }}>
                                            <textarea className="form-control" rows={3} style={{ fontFamily: "monospace", fontSize: "0.8rem", width: "100%", resize: "vertical" }} placeholder="JSON Body Template..." value={body} onChange={(e) => updateAct(\`\${m}|\${url}|\${e.target.value}\`)} />
                                            <div style={{ display: "flex", gap: "0.5rem" }}>
                                              <button type="button" className="btn btn-sm btn-secondary" style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }} onClick={() => updateAct(\`\${m}|\${url}|{ "event": "TorrentCompleted", "name": "\${torrent.name}", "size": \${torrent.size} }\`)}>✨ Insert Torrent JSON Payload</button>
                                            </div>
                                          </div>
                                        )}
                                        
                                        <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap", marginTop: "0.25rem" }}>
                                          <select className="form-control" style={{ width: "140px", fontSize: "0.8rem", padding: "0.2rem" }} value={extra.auth || "none"} onChange={(e) => updateExtra("auth", e.target.value)}>
                                            <option value="none">No Auth</option>
                                            <option value="bearer">Bearer Token</option>
                                            <option value="apikey">X-Api-Key</option>
                                            <option value="basic">Basic Auth</option>
                                          </select>
                                          <input type="text" className="form-control" style={{ width: "120px", fontSize: "0.8rem", padding: "0.2rem" }} placeholder="Register Var (apiRes)" value={extra.register || ""} onChange={(e) => updateExtra("register", e.target.value)} />
                                          <input type="number" className="form-control" style={{ width: "80px", fontSize: "0.8rem", padding: "0.2rem" }} placeholder="Timeout (s)" value={extra.timeout || ""} onChange={(e) => updateExtra("timeout", e.target.value)} />
                                          <label style={{ fontSize: "0.75rem", display: "flex", alignItems: "center", gap: "0.25rem", color: "var(--text-secondary)" }}><input type="checkbox" checked={extra.insecure || false} onChange={(e) => updateExtra("insecure", e.target.checked)} /> Insecure HTTPS</label>
                                          <label style={{ fontSize: "0.75rem", display: "flex", alignItems: "center", gap: "0.25rem", color: "var(--text-secondary)" }}><input type="checkbox" checked={extra.continueOnError || false} onChange={(e) => updateExtra("continueOnError", e.target.checked)} /> Continue on Error</label>
                                        </div>
                                      </div>
                                    );
                                  }

                                  return (
`;

if (content.includes('if (act.type === "http")')) {
  console.log("Already has http");
} else {
  content = content.replace("                                  return (\n                                    <input", replacementStr + "                                    <input");
  fs.writeFileSync('/home/daoneill/src/usr/leecharr/src/Leecharr.Frontend/src/pages/AutomationPage.tsx', content, 'utf8');
  console.log("Success");
}

