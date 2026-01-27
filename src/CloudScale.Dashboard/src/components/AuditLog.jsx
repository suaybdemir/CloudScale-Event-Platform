import { useState, useEffect, memo, useCallback } from 'react';
import axios from 'axios';

const API_BASE = import.meta.env.VITE_API_URL || 'http://localhost:5000/api/dashboard';

const AuditLog = memo(function AuditLog() {
    const [logs, setLogs] = useState([]);
    const [loading, setLoading] = useState(true);

    const fetchAudit = useCallback(async () => {
        try {
            const res = await axios.get(`${API_BASE}/audit-log`);
            const data = Array.isArray(res.data) ? res.data : (res.data.data || []);
            setLogs(data);
        } catch (error) {
            console.error("Error fetching audit log", error);
        } finally {
            setLoading(false);
        }
    }, []);

    useEffect(() => {
        fetchAudit();
        const interval = setInterval(fetchAudit, 5000);
        return () => clearInterval(interval);
    }, [fetchAudit]);

    return (
        <div className="bg-slate-900/50 flex flex-col h-full overflow-hidden">
            <div className="px-5 py-4 border-b border-slate-700/50 flex justify-between items-center bg-slate-800/30 backdrop-blur-sm">
                <div>
                    <h3 className="text-slate-300 text-[10px] uppercase tracking-[0.15em] font-bold">
                        Database Verification
                    </h3>
                    <p className="text-[9px] text-slate-500 font-medium mt-0.5">Direct Cosmos DB Query (Consistency Check)</p>
                </div>
                <button
                    onClick={fetchAudit}
                    className="text-[10px] bg-slate-800 hover:bg-slate-700 text-slate-300 px-3 py-1.5 rounded border border-slate-600/50 transition-all font-bold shadow-sm hover:shadow-md hover:text-white uppercase tracking-wider"
                >
                    Sync Model
                </button>
            </div>

            <div className="flex-grow overflow-y-auto px-0 py-0 custom-scrollbar">
                <table className="w-full text-left border-collapse">
                    <thead className="text-[9px] text-slate-500 uppercase tracking-widest sticky top-0 bg-slate-900/90 border-b border-slate-700/50 z-10 backdrop-blur-md">
                        <tr>
                            <th className="px-5 py-3 font-bold">Event ID</th>
                            <th className="px-5 py-3 font-bold">Type</th>
                            <th className="px-5 py-3 font-bold">Risk Score</th>
                            <th className="px-5 py-3 font-bold text-right">State</th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-800/50">
                        {logs.length === 0 ? (
                            <tr>
                                <td colSpan="4" className="text-center py-16 text-slate-600 font-medium text-xs uppercase tracking-widest">
                                    {loading ? 'Initializing Data Stream...' : 'No Event Persistence Detected'}
                                </td>
                            </tr>
                        ) : (
                            logs.map((log) => (
                                <tr key={log.eventId || log.id} className="hover:bg-slate-800/30 transition-colors group">
                                    <td className="px-5 py-3.5">
                                        <div className="text-[11px] text-slate-300 font-mono group-hover:text-blue-400 transition-colors">{(log.eventId || log.id || '').substring(0, 8)}...</div>
                                        <div className="text-[10px] text-slate-600 font-mono">{new Date(log.createdAt || log.EventData?.CreatedAt).toLocaleTimeString()}</div>
                                    </td>
                                    <td className="px-5 py-3.5">
                                        <span className="text-[10px] font-bold text-slate-400 uppercase tracking-wide bg-slate-800/50 px-2 py-1 rounded border border-slate-700/30">
                                            {log.eventType || log.EventData?.EventType}
                                        </span>
                                    </td>
                                    <td className="px-5 py-3.5">
                                        <div className="flex items-center space-x-2">
                                            <div className="h-1.5 w-16 bg-slate-800 rounded-full overflow-hidden border border-slate-700/30">
                                                <div
                                                    className={`h-full rounded-full shadow-sm ${((log.metadata?.RiskScore) || (log.EventData?.Metadata?.RiskScore) || 0) > 70 ? 'bg-rose-500 shadow-rose-500/50' : 'bg-emerald-500 shadow-emerald-500/50'}`}
                                                    style={{ width: `${((log.metadata?.RiskScore) || (log.EventData?.Metadata?.RiskScore) || 0)}%` }}
                                                ></div>
                                            </div>
                                            <div className="text-[10px] text-slate-500 font-mono font-bold">{((log.metadata?.RiskScore) || (log.EventData?.Metadata?.RiskScore) || 0)}</div>
                                        </div>
                                    </td>
                                    <td className="px-5 py-3.5 text-right">
                                        <span className="inline-flex items-center text-[9px] font-bold text-emerald-400 bg-emerald-500/10 px-2 py-0.5 rounded border border-emerald-500/20 shadow-[0_0_10px_rgba(16,185,129,0.1)]">
                                            PERSISTED
                                        </span>
                                    </td>
                                </tr>
                            ))
                        )}
                    </tbody>
                </table>
            </div>
        </div>
    );
});

export default AuditLog;
