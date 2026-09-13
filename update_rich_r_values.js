const fs = require('fs');
let content = fs.readFileSync('/home/daoneill/src/usr/leecharr/src/Leecharr.Frontend/src/pages/AutomationPage.tsx', 'utf8');

// The goal is to completely rewrite the rendering of the action value inputs starting at:
// `) : act.type === "setShareLimitAction" ? (` or similar, all the way to `)}` before the buttons.
// Since the file might be tricky to parse, I'll use a regex to replace everything between
// `} else if (newType === "notifyArr") { ... updateVisualSteps(copy); }}` and the `</select>` and action values rendering
// actually I'll just write a script that replaces the entire ` {/* Action value rendering */}` block.

let startMarker = `{/* Action value rendering */}`;
let endMarker = `                                <button
                                  type="button"
                                  className="btn btn-sm btn-secondary"
                                  disabled={actIdx === 0}`;

let startIndex = content.indexOf(startMarker);
let endIndex = content.indexOf(endMarker);

if (startIndex === -1 || endIndex === -1) {
  console.log("Markers not found.");
  process.exit(1);
}

const replacement = `                                {/* Action value rendering */}
                                {(() => {
                                  const updateAct = (val) => {
                                    const copy = [...visualSteps];
                                    copy[stepIdx].actions[actIdx].value = val;
                                    updateVisualSteps(copy);
                                  };
                                  const updateExtra = (key, val) => {
                                    const copy = [...visualSteps];
                                    if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                    copy[stepIdx].actions[actIdx].extra[key] = val;
                                    updateVisualSteps(copy);
                                  };
                                  const val = act.value || "";
                                  const extra = act.extra || {};
                                  
                                  if (act.type === "command") {
                                    return (
                                      <select className="form-control" style={{ flex: 1, minWidth: "180px" }} value={val} onChange={(e) => updateAct(e.target.value)}>
                                        <optgroup label="System">
                                          <option value="Backup">Backup - Full DB & Config</option>
                                          <option value="CheckHealth">CheckHealth - System Diagnostic</option>
                                          <option value="UpdateTrackerStats">UpdateTrackerStats - Global Stats</option>
                                          <option value="PurgeDeadTorrents">PurgeDeadTorrents - Clean DB</option>
                                          <option value="RescanTorrents">RescanTorrents - Deep Scan</option>
                                        </optgroup>
                                        <optgroup label="Swarm">
                                          <option value="ForceRecheck">ForceRecheck - All Paused</option>
                                          <option value="CleanIncompleteFolder">CleanIncompleteFolder - Temp Dir</option>
                                          <option value="ScrapeTrackers">ScrapeTrackers - Mass Scrape</option>
                                          <option value="ReannounceAll">ReannounceAll - Force Connect</option>
                                        </optgroup>
                                        <optgroup label="Servarr">
                                          <option value="SyncArr">SyncArr - Push to Servarr</option>
                                          <option value="RssSync">RssSync - Poll RSS Feeds</option>
                                        </optgroup>
                                      </select>
                                    );
                                  }
                                  if (act.type === "setUploadLimit" || act.type === "setDownloadLimit") {
                                    const presets = [0, 500, 1024, 5120, 10240, 51200];
                                    const labels = {0: "Unlimited", 500: "500 KB/s", 1024: "1 MB/s", 5120: "5 MB/s", 10240: "10 MB/s", 51200: "50 MB/s"};
                                    return (
                                      <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.3rem", minWidth: "180px" }}>
                                        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
                                          <input type="number" className="form-control" style={{ flex: 1 }} value={val} onChange={(e) => updateAct(e.target.value)} placeholder="Limit in KB/s" />
                                          {val && <span className="badge" style={{ backgroundColor: "rgba(59, 130, 246, 0.15)", color: "var(--accent)" }}>{val === "0" ? "Unlimited" : (Number(val)/1024).toFixed(2) + " MB/s"}</span>}
                                        </div>
                                        <div style={{ display: "flex", gap: "0.3rem", flexWrap: "wrap" }}>
                                          {presets.map(p => <button key={p} type="button" className="btn btn-secondary" style={{ fontSize: "0.7rem", padding: "0.15rem 0.45rem", backgroundColor: val === String(p) ? "var(--accent)" : undefined, color: val === String(p) ? "#000" : undefined }} onClick={() => updateAct(String(p))}>{labels[p]}</button>)}
                                        </div>
                                      </div>
                                    );
                                  }
                                  if (act.type === "setRatioLimit") {
                                    const presets = ["-1", "1.0", "1.5", "2.0", "2.5", "3.0", "5.0"];
                                    return (
                                      <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.3rem", minWidth: "180px" }}>
                                        <input type="number" step="0.1" className="form-control" value={val} onChange={(e) => updateAct(e.target.value)} placeholder="Ratio (e.g. 2.0)" />
                                        <div style={{ display: "flex", gap: "0.3rem", flexWrap: "wrap" }}>
                                          {presets.map(p => <button key={p} type="button" className="btn btn-secondary" style={{ fontSize: "0.7rem", padding: "0.15rem 0.45rem", backgroundColor: val === p ? "var(--accent)" : undefined, color: val === p ? "#000" : undefined }} onClick={() => updateAct(p)}>{p === "-1" ? "Unlimited" : p + "x"}</button>)}
                                        </div>
                                      </div>
                                    );
                                  }
                                  if (act.type === "setSeedingTimeLimit") {
                                    const presets = [ {v:"-1",l:"Unlimited"}, {v:"60",l:"1 hr"}, {v:"360",l:"6 hrs"}, {v:"1440",l:"1 day"}, {v:"4320",l:"3 days"}, {v:"10080",l:"1 week"} ];
                                    return (
                                      <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.3rem", minWidth: "180px" }}>
                                        <input type="number" className="form-control" value={val} onChange={(e) => updateAct(e.target.value)} placeholder="Minutes" />
                                        <div style={{ display: "flex", gap: "0.3rem", flexWrap: "wrap" }}>
                                          {presets.map(p => <button key={p.v} type="button" className="btn btn-secondary" style={{ fontSize: "0.7rem", padding: "0.15rem 0.45rem", backgroundColor: val === p.v ? "var(--accent)" : undefined, color: val === p.v ? "#000" : undefined }} onClick={() => updateAct(p.v)}>{p.l}</button>)}
                                        </div>
                                      </div>
                                    );
                                  }
                                  if (act.type === "addTag" || act.type === "removeTag") {
                                    const presets = ["Archived", "Seeding-Done", "Plex-Ready", "Cross-Seed", "Private", "VIP", "High-Priority", "Slow-Swarm"];
                                    return (
                                      <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.3rem", minWidth: "180px" }}>
                                        <input type="text" className="form-control" value={val} onChange={(e) => updateAct(e.target.value)} placeholder="Tag name" />
                                        <div style={{ display: "flex", gap: "0.3rem", flexWrap: "wrap" }}>
                                          {presets.map(p => <button key={p} type="button" className="btn btn-secondary" style={{ fontSize: "0.7rem", padding: "0.15rem 0.45rem" }} onClick={() => updateAct(p)}>{p}</button>)}
                                        </div>
                                      </div>
                                    );
                                  }
                                  if (act.type === "setCategory") {
                                    return (
                                      <div style={{ flex: 1, display: "flex", gap: "0.5rem", minWidth: "180px" }}>
                                        <select className="form-control" style={{ flex: 1 }} value={val} onChange={(e) => updateAct(e.target.value)}>
                                          {(categories || []).map(c => <option key={c.id} value={c.name}>📁 {c.name}</option>)}
                                          <option value="custom">✏️ Custom...</option>
                                        </select>
                                        {val === "custom" && <input type="text" className="form-control" style={{ flex: 1 }} value={extra.customCat || ""} onChange={(e) => updateExtra("customCat", e.target.value)} placeholder="Custom Category" />}
                                      </div>
                                    );
                                  }
                                  if (act.type === "setPriority") {
                                    return (
                                      <select className="form-control" style={{ flex: 1, minWidth: "180px" }} value={val || "Normal"} onChange={(e) => updateAct(e.target.value)}>
                                        <option value="High">⚡ High Priority</option>
                                        <option value="Normal">🔹 Normal Priority</option>
                                        <option value="Low">🔻 Low Priority</option>
                                        <option value="DoNotDownload">🚫 Do Not Download (Skip)</option>
                                      </select>
                                    );
                                  }
                                  if (act.type === "setSequentialDownload" || act.type === "setSuperSeeding") {
                                    return (
                                      <select className="form-control" style={{ flex: 1, minWidth: "180px" }} value={val || "true"} onChange={(e) => updateAct(e.target.value)}>
                                        <option value="true">✅ Enabled (True)</option>
                                        <option value="false">❌ Disabled (False)</option>
                                      </select>
                                    );
                                  }
                                  if (act.type === "notifyArr" || act.type === "syncArr") {
                                    return (
                                      <select className="form-control" style={{ flex: 1, minWidth: "180px" }} value={val} onChange={(e) => updateAct(e.target.value)}>
                                        <option value="">🌐 All Connected Servarr Instances</option>
                                        <option value="Sonarr">📺 Sonarr (TV Shows)</option>
                                        <option value="Radarr">🎬 Radarr (Movies)</option>
                                        <option value="Lidarr">🎵 Lidarr (Music)</option>
                                        <option value="Readarr">📚 Readarr (Books)</option>
                                        <option value="Whisparr">🔞 Whisparr (Adult)</option>
                                      </select>
                                    );
                                  }
                                  if (act.type === "sendNotification") {
                                    const chips = ["\${torrent.name}", "\${torrent.size}", "\${torrent.ratio}", "\${torrent.category}", "\${torrent.state}"];
                                    return (
                                      <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.3rem", minWidth: "180px" }}>
                                        <input type="text" className="form-control" value={val} onChange={(e) => updateAct(e.target.value)} placeholder="Notification Message" />
                                        <div style={{ display: "flex", gap: "0.3rem", flexWrap: "wrap" }}>
                                          {chips.map(c => <button key={c} type="button" className="btn btn-secondary" style={{ fontSize: "0.7rem", padding: "0.15rem 0.45rem" }} onClick={() => updateAct(val + " " + c)}>{c}</button>)}
                                        </div>
                                      </div>
                                    );
                                  }
                                  if (act.type === "sendDiscordWebhook") {
                                    const colors = [ {v:"#22c55e", l:"Green"}, {v:"#3b82f6", l:"Blue"}, {v:"#f97316", l:"Orange"}, {v:"#ef4444", l:"Red"}, {v:"#a855f7", l:"Purple"} ];
                                    return (
                                      <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.5rem", minWidth: "180px" }}>
                                        <div style={{ display: "flex", gap: "0.5rem" }}>
                                          <input type="text" className="form-control" style={{ flex: 2 }} placeholder="Webhook URL" value={val.split('|')[0] || ""} onChange={(e) => updateAct(\`\${e.target.value}|\${val.split('|')[1] || ""}|\${val.split('|')[2] || ""}\`)} />
                                          <input type="color" className="form-control" style={{ width: "40px", padding: "0.1rem" }} value={val.split('|')[1] || "#3b82f6"} onChange={(e) => updateAct(\`\${val.split('|')[0] || ""}|\${e.target.value}|\${val.split('|')[2] || ""}\`)} />
                                          <input type="text" className="form-control" style={{ flex: 1 }} placeholder="Embed Title" value={val.split('|')[2] || ""} onChange={(e) => updateAct(\`\${val.split('|')[0] || ""}|\${val.split('|')[1] || ""}|\${e.target.value}\`)} />
                                        </div>
                                        <div style={{ display: "flex", gap: "0.3rem", flexWrap: "wrap" }}>
                                          {colors.map(c => <button key={c.v} type="button" className="btn btn-secondary" style={{ fontSize: "0.7rem", padding: "0.15rem 0.45rem", borderBottom: \`2px solid \${c.v}\` }} onClick={() => updateAct(\`\${val.split('|')[0] || ""}|\${c.v}|\${val.split('|')[2] || ""}\`)}>{c.l}</button>)}
                                        </div>
                                      </div>
                                    );
                                  }
                                  if (act.type === "sendTelegramMessage") {
                                    return (
                                      <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.5rem", minWidth: "180px" }}>
                                        <div style={{ display: "flex", gap: "0.5rem" }}>
                                          <input type="text" className="form-control" style={{ flex: 1 }} placeholder="Bot Token" value={val.split('|')[0] || ""} onChange={(e) => updateAct(\`\${e.target.value}|\${val.split('|')[1] || ""}|\${val.split('|')[2] || ""}\`)} />
                                          <input type="text" className="form-control" style={{ flex: 1 }} placeholder="Chat ID" value={val.split('|')[1] || ""} onChange={(e) => updateAct(\`\${val.split('|')[0] || ""}|\${e.target.value}|\${val.split('|')[2] || ""}\`)} />
                                        </div>
                                        <input type="text" className="form-control" placeholder="Message template..." value={val.split('|')[2] || ""} onChange={(e) => updateAct(\`\${val.split('|')[0] || ""}|\${val.split('|')[1] || ""}|\${e.target.value}\`)} />
                                      </div>
                                    );
                                  }
                                  if (act.type === "sendNtfy") {
                                    return (
                                      <div style={{ flex: 1, display: "flex", gap: "0.5rem", minWidth: "180px" }}>
                                        <input type="text" className="form-control" style={{ flex: 2 }} placeholder="Topic URL" value={val.split('|')[0] || ""} onChange={(e) => updateAct(\`\${e.target.value}|\${val.split('|')[1] || "3"}\`)} />
                                        <select className="form-control" style={{ flex: 1 }} value={val.split('|')[1] || "3"} onChange={(e) => updateAct(\`\${val.split('|')[0] || ""}|\${e.target.value}\`)}>
                                          <option value="1">Min Priority</option>
                                          <option value="2">Low Priority</option>
                                          <option value="3">Default Priority</option>
                                          <option value="4">High Priority</option>
                                          <option value="5">Urgent Priority</option>
                                        </select>
                                      </div>
                                    );
                                  }
                                  if (act.type === "createHardlink" || act.type === "createSymlink") {
                                    const presets = ["/media/movies", "/media/tv", "/data/completed"];
                                    return (
                                      <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.5rem", minWidth: "180px" }}>
                                        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
                                          <input type="text" className="form-control" style={{ flex: 1 }} placeholder="Dest Dir" value={val} onChange={(e) => updateAct(e.target.value)} />
                                          <label style={{ fontSize: "0.8rem", display: "flex", alignItems: "center", gap: "0.25rem", cursor: "pointer", color: "var(--text-secondary)" }}><input type="checkbox" checked={extra.preserveHierarchy !== false} onChange={(e) => updateExtra("preserveHierarchy", e.target.checked)} /> Preserve folder hierarchy</label>
                                        </div>
                                        <div style={{ display: "flex", gap: "0.3rem", flexWrap: "wrap" }}>
                                          {presets.map(p => <button key={p} type="button" className="btn btn-secondary" style={{ fontSize: "0.7rem", padding: "0.15rem 0.45rem" }} onClick={() => updateAct(p)}>{p}</button>)}
                                        </div>
                                      </div>
                                    );
                                  }
                                  if (act.type === "cleanExtensions") {
                                    const presets = [".nfo", ".txt", ".sample", ".exe", ".url", ".jpg"];
                                    return (
                                      <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.5rem", minWidth: "180px" }}>
                                        <div style={{ display: "flex", gap: "0.5rem" }}>
                                          <input type="text" className="form-control" style={{ flex: 2 }} placeholder="Extensions (.nfo, .txt)" value={val} onChange={(e) => updateAct(e.target.value)} />
                                          <input type="text" className="form-control" style={{ flex: 1 }} placeholder="Max size (e.g. < 50 MB)" value={extra.maxSize || ""} onChange={(e) => updateExtra("maxSize", e.target.value)} />
                                        </div>
                                        <div style={{ display: "flex", gap: "0.3rem", flexWrap: "wrap" }}>
                                          {presets.map(p => <button key={p} type="button" className="btn btn-secondary" style={{ fontSize: "0.7rem", padding: "0.15rem 0.45rem" }} onClick={() => updateAct(val ? val + ", " + p : p)}>{p}</button>)}
                                        </div>
                                      </div>
                                    );
                                  }
                                  if (act.type === "setFilePermissions") {
                                    const presets = [ {v:"0775",l:"0775 (rwxrwxr-x)"}, {v:"0755",l:"0755 (rwxr-xr-x)"}, {v:"0664",l:"0664 (rw-rw-r--)"}, {v:"0644",l:"0644 (rw-r--r--)"} ];
                                    return (
                                      <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.5rem", minWidth: "180px" }}>
                                        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
                                          <input type="text" className="form-control" style={{ flex: 1 }} placeholder="Octal (e.g. 0755)" value={val} onChange={(e) => updateAct(e.target.value)} />
                                          <label style={{ fontSize: "0.8rem", display: "flex", alignItems: "center", gap: "0.25rem", cursor: "pointer", color: "var(--text-secondary)" }}><input type="checkbox" checked={extra.recursive !== false} onChange={(e) => updateExtra("recursive", e.target.checked)} /> Recursive</label>
                                        </div>
                                        <div style={{ display: "flex", gap: "0.3rem", flexWrap: "wrap" }}>
                                          {presets.map(p => <button key={p.v} type="button" className="btn btn-secondary" style={{ fontSize: "0.7rem", padding: "0.15rem 0.45rem", backgroundColor: val === p.v ? "var(--accent)" : undefined, color: val === p.v ? "#000" : undefined }} onClick={() => updateAct(p.v)}>{p.l}</button>)}
                                        </div>
                                      </div>
                                    );
                                  }
                                  if (act.type === "setShareLimitAction") {
                                    return (
                                      <select className="form-control" style={{ flex: 1, minWidth: "180px" }} value={val || "Pause"} onChange={(e) => updateAct(e.target.value)}>
                                        <option value="Pause">⏸️ Pause Torrent</option>
                                        <option value="Stop">⏹️ Stop Torrent</option>
                                        <option value="Remove">🗑️ Remove from Client (Keep Data)</option>
                                        <option value="RemoveAndDelete">🔥 Remove & Delete Files from Disk</option>
                                      </select>
                                    );
                                  }
                                  if (act.type === "delay" || act.type === "sleep") {
                                    const presets = [{v:"5",l:"5s"}, {v:"15",l:"15s"}, {v:"30",l:"30s"}, {v:"60",l:"1 min"}, {v:"300",l:"5 min"}];
                                    return (
                                      <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.3rem", minWidth: "180px" }}>
                                        <input type="number" className="form-control" value={val} onChange={(e) => updateAct(e.target.value)} placeholder="Seconds" />
                                        <div style={{ display: "flex", gap: "0.3rem", flexWrap: "wrap" }}>
                                          {presets.map(p => <button key={p.v} type="button" className="btn btn-secondary" style={{ fontSize: "0.7rem", padding: "0.15rem 0.45rem", backgroundColor: val === p.v ? "var(--accent)" : undefined, color: val === p.v ? "#000" : undefined }} onClick={() => updateAct(p.v)}>{p.l}</button>)}
                                        </div>
                                      </div>
                                    );
                                  }
                                  if (act.type === "evalMath") {
                                    const chips = ["\${torrent.size}", "\${torrent.ratio}", "\${inputs.min}"];
                                    return (
                                      <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.5rem", minWidth: "180px" }}>
                                        <div style={{ display: "flex", gap: "0.5rem" }}>
                                          <input type="text" className="form-control" style={{ flex: 2 }} placeholder="Expression (e.g. torrent.size * 2)" value={val.split('|')[0] || ""} onChange={(e) => updateAct(\`\${e.target.value}|\${val.split('|')[1] || ""}\`)} />
                                          <input type="text" className="form-control" style={{ flex: 1 }} placeholder="Target Var" value={val.split('|')[1] || ""} onChange={(e) => updateAct(\`\${val.split('|')[0] || ""}|\${e.target.value}\`)} />
                                        </div>
                                        <div style={{ display: "flex", gap: "0.3rem", flexWrap: "wrap" }}>
                                          {chips.map(c => <button key={c} type="button" className="btn btn-secondary" style={{ fontSize: "0.7rem", padding: "0.15rem 0.45rem" }} onClick={() => updateAct(\`\${val.split('|')[0] || ""} \${c}|\${val.split('|')[1] || ""}\`)}>{c}</button>)}
                                        </div>
                                      </div>
                                    );
                                  }
                                  if (act.type === "remove") {
                                    return (
                                      <label style={{ flex: 1, display: "flex", alignItems: "center", gap: "0.5rem", fontSize: "0.85rem", cursor: "pointer", color: "var(--color-danger, #ff6b6b)" }}>
                                        <input type="checkbox" checked={act.deleteData || false} onChange={(e) => {
                                          const copy = [...visualSteps];
                                          copy[stepIdx].actions[actIdx].deleteData = e.target.checked;
                                          updateVisualSteps(copy);
                                        }} />
                                        🗑️ Also permanently delete downloaded files from disk
                                      </label>
                                    );
                                  }
                                  if (["pause", "resume", "recheck", "reannounce", "boostTracker", "calculateChecksum", "reannounceAll", "exportTorrent"].includes(act.type)) {
                                    return <span style={{ flex: 1, fontSize: "0.85rem", color: "var(--text-muted)", paddingLeft: "0.25rem" }}>✨ Auto-applies to active swarm & torrent</span>;
                                  }

                                  return (
                                    <input
                                      type={["setUploadLimit", "setDownloadLimit", "setRatioLimit", "setSeedingTimeLimit", "delay", "retryStep"].includes(act.type) ? "number" : "text"}
                                      className="form-control"
                                      style={{ flex: 1, minWidth: "180px" }}
                                      value={val}
                                      onChange={(e) => updateAct(e.target.value)}
                                      placeholder={actDef?.placeholder || "Action value"}
                                    />
                                  );
                                })()}
`;

content = content.substring(0, startIndex) + replacement + content.substring(endIndex);
fs.writeFileSync('/home/daoneill/src/usr/leecharr/src/Leecharr.Frontend/src/pages/AutomationPage.tsx', content, 'utf8');
console.log("Success");
