import React, { memo } from 'react';

const TopUsers = memo(function TopUsers({ users }) {
    return (
        <div className="flex flex-col h-full bg-transparent">
            <div className="flex-1 overflow-auto custom-scrollbar p-1">
                {users.length === 0 ? (
                    <div className="text-center py-12 text-slate-500 font-medium text-sm">
                        No user activity recorded.
                    </div>
                ) : (
                    users.map((user, idx) => (
                        <div
                            key={idx}
                            className="flex items-center justify-between p-4 border-b border-slate-700/30 last:border-0 hover:bg-slate-700/20 transition-colors cursor-default rounded-lg mb-1"
                        >
                            <div className="flex items-center space-x-4">
                                <div className={`
                                    w-6 h-6 rounded-md flex items-center justify-center text-xs font-bold
                                    ${idx === 0 ? 'bg-amber-500/20 text-amber-500 border border-amber-500/30' : ''}
                                    ${idx === 1 ? 'bg-slate-600/30 text-slate-300 border border-slate-600/50' : ''}
                                    ${idx === 2 ? 'bg-orange-900/40 text-orange-400 border border-orange-800/50' : ''}
                                    ${idx > 2 ? 'text-slate-500 bg-slate-800/30' : ''}
                                `}>
                                    {idx + 1}
                                </div>
                                <div>
                                    <div className="text-sm font-semibold text-slate-200 font-mono">
                                        {user.id}
                                    </div>
                                    <div className="text-[10px] text-slate-500 font-medium">
                                        {user.eventCount} events
                                    </div>
                                </div>
                            </div>

                            <div className="text-right">
                                <div className={`text-sm font-bold font-mono ${user.totalScore > 80 ? 'text-rose-400' : 'text-slate-300'}`}>
                                    {user.totalScore}
                                </div>
                                <div className="text-[9px] text-slate-600 uppercase tracking-wide">
                                    Risk Score
                                </div>
                            </div>
                        </div>
                    ))
                )}
            </div>
        </div>
    );
});

export default TopUsers;
