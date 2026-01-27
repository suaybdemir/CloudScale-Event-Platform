import React, { memo } from 'react';

/**
 * EventTypeBreakdown - Distribution View (Minimalist)
 */
const EventTypeBreakdown = memo(function EventTypeBreakdown({ distribution = {} }) {
    const eventTypes = [
        { name: 'Page Views', count: distribution.page_view || 0, color: 'bg-indigo-500' },
        { name: 'User Actions', count: distribution.user_action || 0, color: 'bg-violet-500' },
        { name: 'Purchases', count: distribution.purchase || 0, color: 'bg-emerald-500' },
        { name: 'Other', count: distribution.check_cart_status || 0, color: 'bg-slate-400' },
    ];

    const total = eventTypes.reduce((sum, e) => sum + e.count, 0);

    // Add percentages
    eventTypes.forEach(e => {
        e.percentage = total > 0 ? (e.count / total) * 100 : 0;
    });

    return (
        <div className="h-full w-full flex flex-col justify-center px-6">
            {/* Simple Bar Chart */}
            <div className="space-y-6">
                {eventTypes.map((type, idx) => (
                    <div key={idx} className="group">
                        <div className="flex justify-between text-xs mb-2 align-bottom">
                            <span className="text-slate-300 font-bold tracking-wide">{type.name}</span>
                            <div className="text-right">
                                <span className="text-white font-mono font-bold text-sm tracking-tight">{type.count.toLocaleString()}</span>
                                <span className="text-slate-500 text-[10px] ml-1 font-mono">({type.percentage.toFixed(1)}%)</span>
                            </div>
                        </div>
                        <div className="h-2 bg-slate-700/50 rounded-full overflow-hidden border border-slate-700/30">
                            <div
                                className={`h-full ${type.color} rounded-full transition-all duration-700 ease-out shadow-[0_0_10px_rgba(0,0,0,0.3)]`}
                                style={{ width: `${type.percentage}%` }}
                            ></div>
                        </div>
                    </div>
                ))}
            </div>

            {/* Total */}
            <div className="mt-8 pt-6 border-t border-slate-700/50 flex justify-between items-center">
                <span className="text-slate-500 text-[10px] font-bold uppercase tracking-[0.1em]">Total Processed Events</span>
                <span className="text-slate-200 font-bold text-xl font-mono">{total.toLocaleString()}</span>
            </div>
        </div>
    );
});

export default EventTypeBreakdown;
