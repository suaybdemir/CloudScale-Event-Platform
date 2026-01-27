import React, { memo } from 'react';

const ThroughputMeter = memo(function ThroughputMeter({ value, max = 2000 }) {
    // Normalize value to 0-? range (not capped at 1 anymore)
    const normalized = Math.max(value / max, 0);
    const cappedForCircle = Math.min(normalized, 1.1); // Let it go slightly over 180deg for effect

    // SVG Geometry for a semi-circle
    const radius = 80;
    const stroke = 12;
    const normalizedRadius = radius - stroke * 2;
    const arcLength = Math.PI * normalizedRadius;
    const fillLength = cappedForCircle * arcLength;

    const isOverloaded = normalized > 1;

    return (
        <div className="h-full flex flex-col items-center justify-center relative">
            <div className={`relative w-64 h-32 overflow-hidden ${isOverloaded ? 'animate-pulse' : ''}`}>
                <svg
                    height="100%"
                    width="100%"
                    viewBox="0 0 200 110"
                    className="overflow-visible"
                >
                    {/* Defs for Gradients */}
                    <defs>
                        <linearGradient id="gaugeGradient" x1="0%" y1="0%" x2="100%" y2="0%">
                            <stop offset="0%" stopColor="#3b82f6" />
                            <stop offset="70%" stopColor="#8b5cf6" />
                            <stop offset="100%" stopColor={isOverloaded ? "#ff0000" : "#ec4899"} />
                        </linearGradient>
                        <filter id="glow" x="-20%" y="-20%" width="140%" height="140%">
                            <feGaussianBlur stdDeviation={isOverloaded ? "6" : "4"} result="coloredBlur" />
                            <feMerge>
                                <feMergeNode in="coloredBlur" />
                                <feMergeNode in="SourceGraphic" />
                            </feMerge>
                        </filter>
                    </defs>

                    {/* Background Track */}
                    <path
                        d={`M 20 100 A ${normalizedRadius} ${normalizedRadius} 0 0 1 180 100`}
                        fill="none"
                        stroke="#1e293b"
                        strokeWidth={stroke}
                        strokeLinecap="round"
                    />

                    {/* Filled Track */}
                    <path
                        d={`M 20 100 A ${normalizedRadius} ${normalizedRadius} 0 0 1 180 100`}
                        fill="none"
                        stroke="url(#gaugeGradient)"
                        strokeWidth={stroke}
                        strokeLinecap="round"
                        strokeDasharray={`${fillLength} ${arcLength * 2}`}
                        strokeDashoffset="0"
                        filter="url(#glow)"
                        className="transition-[stroke-dasharray,stroke] duration-700 ease-out"
                    />
                </svg>

                {/* Needle / Value Text Overlay */}
                <div className="absolute inset-0 flex flex-col items-center justify-end pb-0">
                    <div className={`text-4xl font-bold font-mono tracking-tighter drop-shadow-lg ${isOverloaded ? 'text-rose-500' : 'text-white'}`}>
                        {Math.round(value).toLocaleString()}
                    </div>
                    <div className="text-xs text-slate-400 font-bold uppercase tracking-widest mt-1">
                        Events / Sec
                    </div>
                </div>
            </div>

            {/* Status Indicator */}
            <div className={`mt-6 flex items-center space-x-2 px-4 py-2 rounded-full border transition-all duration-500 ${isOverloaded ? 'bg-rose-500/20 border-rose-500/50 scale-110' : 'bg-slate-800/50 border-slate-700/50'}`}>
                <span className={`w-2 h-2 rounded-full ${isOverloaded ? 'bg-rose-500 shadow-[0_0_12px_#f43f5e] animate-ping' : (normalized > 0.8 ? 'bg-amber-500' : 'bg-emerald-500')} transition-colors duration-500`}></span>
                <span className={`text-[10px] uppercase tracking-widest font-bold ${isOverloaded ? 'text-rose-400' : 'text-slate-300'}`}>
                    System Load: {Math.round(normalized * 100)}% {isOverloaded ? '(OVERLOADED)' : ''}
                </span>
            </div>
        </div>
    );
});

export default ThroughputMeter;
