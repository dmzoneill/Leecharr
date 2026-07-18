import React, { useState } from 'react';

export const SpeedSchedule: React.FC = () => {
  const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
  const hours = Array.from({ length: 24 }, (_, i) => `${i.toString().padStart(2, '0')}:00`);

  // 0: Normal, 1: Throttled, 2: Suspended/Paused
  const [scheduleGrid, setScheduleGrid] = useState<number[][]>(() =>
    Array(7).fill(0).map(() => Array(24).fill(0))
  );

  const toggleCell = (dayIdx: number, hourIdx: number) => {
    setScheduleGrid(prev => {
      const next = prev.map(row => [...row]);
      next[dayIdx][hourIdx] = (next[dayIdx][hourIdx] + 1) % 3;
      return next;
    });
  };

  const getCellClass = (mode: number) => {
    switch (mode) {
      case 0: return 'cell-normal';
      case 1: return 'cell-throttled';
      case 2: return 'cell-paused';
      default: return 'cell-normal';
    }
  };

  return (
    <div className="schedule-page">
      <div className="page-header">
        <h2>24x7 Speed Throttling Schedule</h2>
        <p className="text-muted">Configure hourly download and upload bandwidth speed limits across the week.</p>
      </div>

      <div className="schedule-legend">
        <span className="legend-item"><span className="legend-box cell-normal" /> Normal Speed (Full Bandwidth)</span>
        <span className="legend-item"><span className="legend-box cell-throttled" /> Throttled (Scheduled Limits)</span>
        <span className="legend-item"><span className="legend-box cell-paused" /> Suspended / Paused</span>
      </div>

      <div className="schedule-grid-wrapper">
        <table className="schedule-table">
          <thead>
            <tr>
              <th className="day-header">Day</th>
              {hours.map((h, i) => (
                <th key={i} className="hour-header">{i}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {days.map((day, dayIdx) => (
              <tr key={dayIdx}>
                <td className="day-name">{day}</td>
                {hours.map((_, hourIdx) => {
                  const mode = scheduleGrid[dayIdx][hourIdx];
                  return (
                    <td
                      key={hourIdx}
                      className={`schedule-cell ${getCellClass(mode)}`}
                      onClick={() => toggleCell(dayIdx, hourIdx)}
                      title={`${day} @ ${hours[hourIdx]}: ${mode === 0 ? 'Normal' : mode === 1 ? 'Throttled' : 'Paused'}`}
                    />
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
};
export default SpeedSchedule;
