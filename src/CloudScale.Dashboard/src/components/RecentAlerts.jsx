import React, { memo } from 'react';

const RecentAlerts = memo(function RecentAlerts({ alerts }) {
    return (
        <div className="flex flex-col h-full bg-slate-900/50">
            <div className="flex-1 overflow-auto custom-scrollbar">
                <table className="w-full text-sm text-left border-collapse">
                    <thead className="text-[10px] text-slate-500 uppercase bg-slate-800/80 sticky top-0 backdrop-blur-sm z-10">
                        <tr>
                            <th className="px-6 py-3 font-bold tracking-wider">Time</th>
                            <th className="px-6 py-3 font-bold tracking-wider">Event</th>
                            <th className="px-6 py-3 font-bold tracking-wider">User</th>
                            <th className="px-6 py-3 font-bold tracking-wider text-right">Risk Score</th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-800">
                        {alerts.length === 0 ? (
                            <tr>
                                <td colSpan="4" className="py-12 text-center text-slate-500 font-medium">
                                    No active security threats logged.
                                </td>
                            </tr>
                        ) : (
                            alerts.map((alert, idx) => (
                                <tr key={alert.id || idx} className="hover:bg-slate-800/30 transition-colors group">
                                    <td className="px-6 py-4 text-slate-400 font-mono text-xs whitespace-nowrap">
                                        {new Date(alert.createdAt || Date.now()).toLocaleTimeString()}
                                    </td>
                                    <td className="px-6 py-4">
                                        <div className="text-slate-200 font-medium group-hover:text-blue-400 transition-colors">{alert.eventType}</div>
                                        <div className="text-xs text-slate-500 mt-0.5">{alert.riskReason}</div>
                                    </td>
                                    <td className="px-6 py-4">
                                        <span className="font-mono text-xs text-slate-400 bg-slate-800 px-2 py-1 rounded border border-slate-700/50">
                                            {alert.userId || alert.clientIp || 'Unknown'}
                                        </span>
                                    </td>
                                    <td className="px-6 py-4 text-right align-middle">
                                        <div className="flex items-center justify-end space-x-3">
                                            <div className="w-24 bg-slate-800 rounded-full h-1.5 overflow-hidden">
                                                <div
                                                    className={`h-full rounded-full shadow-lg ${alert.riskScore > 80 ? 'bg-rose-500 shadow-rose-500/50' :
                                                        alert.riskScore > 50 ? 'bg-amber-500 shadow-amber-500/50' : 'bg-blue-500 shadow-blue-500/50'
                                                        }`}
                                                    style={{ width: `${Math.min(alert.riskScore || 0, 100)}%` }}
                                                ></div>
                                            </div>
                                            <span className={`text-xs font-bold w-8 text-right font-mono ${alert.riskScore > 80 ? 'text-rose-400' : 'text-slate-400'}`}>
                                                {alert.riskScore || 0}
                                            </span>
                                        </div>
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

export default RecentAlerts;
