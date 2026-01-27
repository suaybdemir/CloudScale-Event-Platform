import React, { memo } from 'react';

const StatsCard = memo(function StatsCard({ title, value, subtext, indicatorColor = "bg-slate-400" }) {
    return (
        <div className="bg-slate-800/40 backdrop-blur-md rounded-xl p-6 border border-slate-700/50 hover:bg-slate-800/60 transition-colors duration-300">
            <div className="flex items-center space-x-2 mb-3">
                <div className={`w-2 h-2 rounded-full ${indicatorColor}`}></div>
                <h3 className="text-slate-400 text-xs font-bold uppercase tracking-widest">{title}</h3>
            </div>

            <div className="mt-1">
                <p className="text-3xl md:text-4xl font-bold text-white tracking-tight font-mono">
                    {value}
                </p>
            </div>

            {subtext && (
                <div className="mt-3 text-[10px] uppercase tracking-wider text-slate-500 font-medium">
                    {subtext}
                </div>
            )}
        </div>
    );
});

export default StatsCard;
