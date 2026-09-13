const fs = require('fs');
let content = fs.readFileSync('/home/daoneill/src/usr/leecharr/src/Leecharr.Frontend/src/pages/AutomationPage.tsx', 'utf8');

// Insert states
const stateTarget = `  const [isRunningId, setIsRunningId] = useState<number | null>(null);`;
const stateReplacement = `  const [isRunningId, setIsRunningId] = useState<number | null>(null);

  const [simModalOpen, setSimModalOpen] = useState(false);
  const [simTargetScriptId, setSimTargetScriptId] = useState<number | "">("");
  const [simLogs, setSimLogs] = useState<string[]>([]);
  
  function runClientSimulator() {
    if (!simTargetScriptId) return;
    const script = scriptList.find(s => s.id === simTargetScriptId);
    if (!script) return;
    
    setSimLogs(["[SYSTEM] Starting client-side simulation...", \`[SYSTEM] Target: \${script.name}\`]);
    
    // Simulate a payload
    const simTorrent = {
      name: "Simulated.Movie.1080p.x264",
      size: 15 * 1024 * 1024 * 1024,
      isPrivate: true,
      isComplete: true,
      status: 'Seeding',
      category: 'Movies',
      ratio: 1.5,
      progress: 100,
      downloadSpeed: 0,
      uploadSpeed: 5242880,
      seeders: 5,
      leechers: 10,
      tracker: 'tracker.simulated.net',
      savePath: '/downloads/simulated'
    };
    
    setSimLogs(prev => [...prev, \`[EVENT] Simulated payload: \${JSON.stringify(simTorrent)}\`]);
    
    if (script.language !== "Yaml" && script.language !== 1) {
      setSimLogs(prev => [...prev, "[ERROR] Client simulator only supports Visual Pipelines (YAML).", "[SYSTEM] Simulation aborted."]);
      return;
    }
    
    const steps = yamlToVisualSteps(script.code || "");
    setSimLogs(prev => [...prev, \`[SYSTEM] Parsed \${steps.length} visual steps.\`]);
    
    for (let i = 0; i < steps.length; i++) {
      const step = steps[i];
      setSimLogs(prev => [...prev, \`\\n[STEP \${i+1}] Evaluating: \${step.name}\`]);
      
      let matched = true;
      if (step.conditionEnabled) {
        let lValStr = step.conditionLeft;
        let rValStr = step.conditionRight;
        
        // rudimentary substitution
        const resolveVal = (valStr) => {
          if (valStr.includes("\${torrent.size}")) return simTorrent.size;
          if (valStr.includes("\${torrent.ratio}")) return simTorrent.ratio;
          if (valStr.includes("\${torrent.progress}")) return simTorrent.progress;
          if (valStr.includes("\${torrent.seeders}")) return simTorrent.seeders;
          if (valStr.includes("\${torrent.leechers}")) return simTorrent.leechers;
          if (valStr.includes("\${torrent.isPrivate}")) return simTorrent.isPrivate;
          if (valStr.includes("\${torrent.isComplete}")) return simTorrent.isComplete;
          if (valStr.includes("\${torrent.status}")) return \`'\${simTorrent.status}'\`;
          if (valStr.includes("\${torrent.category}")) return \`'\${simTorrent.category}'\`;
          if (valStr.includes("\${torrent.name}")) return \`'\${simTorrent.name}'\`;
          if (valStr.includes("\${torrent.tracker}")) return \`'\${simTorrent.tracker}'\`;
          
          if (!isNaN(Number(valStr)) && valStr.trim() !== "") return Number(valStr);
          if (valStr === "true") return true;
          if (valStr === "false") return false;
          return valStr;
        };
        
        const lVal = resolveVal(lValStr);
        const rVal = resolveVal(rValStr);
        
        setSimLogs(prev => [...prev, \`[CONDITION] \${lValStr} \${step.conditionOp} \${rValStr} -> \${lVal} \${step.conditionOp} \${rVal}\`]);
        
        if (step.conditionOp === "==") matched = lVal == rVal;
        else if (step.conditionOp === "!=") matched = lVal != rVal;
        else if (step.conditionOp === ">") matched = lVal > rVal;
        else if (step.conditionOp === "<") matched = lVal < rVal;
        else if (step.conditionOp === ">=") matched = lVal >= rVal;
        else if (step.conditionOp === "<=") matched = lVal <= rVal;
      }
      
      if (matched) {
        setSimLogs(prev => [...prev, \`[MATCH] Step '\${step.name}' matched. Dispatching \${step.actions.length} actions...\`]);
        for (const act of step.actions) {
          setSimLogs(prev => [...prev, \`  -> [ACTION] \${act.type} (Value: \${act.value || "None"})\`]);
        }
      } else {
        setSimLogs(prev => [...prev, \`[SKIP] Step '\${step.name}' condition failed.\`]);
      }
    }
    
    setSimLogs(prev => [...prev, "\\n[SYSTEM] Simulation complete."]);
  }`;

if (content.includes("runClientSimulator")) {
    console.log("Already added");
} else {
    content = content.replace(stateTarget, stateReplacement);
}

// Add the button
const btnTarget = `              <button className="btn btn-secondary" onClick={() => openNewScript("JavaScript")}>
                💻 + JavaScript Script
              </button>`;
const btnReplacement = `              <button className="btn btn-secondary" onClick={() => openNewScript("JavaScript")}>
                💻 + JavaScript Script
              </button>
              <button className="btn btn-secondary" onClick={() => setSimModalOpen(true)}>
                🧪 Test / Dry Run Pipeline
              </button>`;
content = content.replace(btnTarget, btnReplacement);

// Add the modal at the end of the file
const modalHtml = `
      {/* SIMULATOR MODAL */}
      {simModalOpen && (
        <div className="modal-overlay">
          <div className="modal panel" style={{ width: "100%", maxWidth: "800px", padding: "1.75rem", backgroundColor: "var(--bg-secondary)" }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "1rem" }}>
              <h3 style={{ margin: 0, fontSize: "1.25rem", fontWeight: 700 }}>🧪 Client-Side Pipeline Simulator</h3>
              <button type="button" className="btn btn-sm btn-secondary" style={{ width: "32px", height: "32px", padding: 0 }} onClick={() => setSimModalOpen(false)}>✕</button>
            </div>
            
            <div style={{ display: "flex", gap: "0.75rem", marginBottom: "1rem" }}>
              <select className="form-control" style={{ flex: 1 }} value={simTargetScriptId} onChange={(e) => setSimTargetScriptId(Number(e.target.value))}>
                <option value="">Select a pipeline to simulate...</option>
                {scriptList.filter(s => s.language === "Yaml" || s.language === 1).map(s => (
                  <option key={s.id} value={s.id}>{s.name}</option>
                ))}
              </select>
              <button className="btn btn-primary" onClick={runClientSimulator} disabled={!simTargetScriptId}>▶️ Run Simulation Trace</button>
            </div>
            
            <pre style={{
              backgroundColor: "#000",
              color: "#34d399",
              padding: "1rem",
              borderRadius: "6px",
              minHeight: "300px",
              maxHeight: "500px",
              overflowY: "auto",
              fontSize: "0.8rem",
              fontFamily: "monospace"
            }}>
              {simLogs.length === 0 ? "Select a pipeline and run to view trace logs..." : simLogs.join("\\n")}
            </pre>
          </div>
        </div>
      )}
`;

if (!content.includes("{/* SIMULATOR MODAL */}")) {
  content = content.replace(/    <\/div>\n  \);\n}\n$/, modalHtml + `    </div>\n  );\n}\n`);
}

fs.writeFileSync('/home/daoneill/src/usr/leecharr/src/Leecharr.Frontend/src/pages/AutomationPage.tsx', content, 'utf8');
console.log("Success");
