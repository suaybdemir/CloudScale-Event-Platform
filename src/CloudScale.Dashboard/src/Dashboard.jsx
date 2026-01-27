import { useState, useEffect, useMemo, useCallback } from 'react';
import axios from 'axios';
import StatsCard from './components/StatsCard';
import ThroughputMeter from './components/ThroughputMeter';
import RecentAlerts from './components/RecentAlerts';
import TopUsers from './components/TopUsers';
import EventTypeBreakdown from './components/EventTypeBreakdown';
import AuditLog from './components/AuditLog';

const getApiBase = () => {
    // If we're in a browser, use the current host, otherwise fallback
    if (typeof window !== 'undefined') {
        const host = window.location.hostname;
        return `http://${host}:5000/api/dashboard`;
    }
    return 'http://localhost:5000/api/dashboard';
};

const API_BASE = getApiBase();

export default function Dashboard() {
    const [stats, setStats] = useState({
        totalEvents: 0,
        fraudCount: 0,
        queueDepth: 0,
        successRate: 100,
        distribution: {},
        performance: { avgLatencyMs: 0, uptimeSeconds: 0 },
        system: { apiStatus: 'Healthy', processorStatus: 'Healthy', apiReplicas: 0, processorReplicas: 0, maxConcurrent: 0 }
    });
    const [currentThroughput, setCurrentThroughput] = useState(0);

    const [alerts, setAlerts] = useState([]);
    const [topUsers, setTopUsers] = useState([]);
    const [loading, setLoading] = useState(true);
    const [activeTab, setActiveTab] = useState('alerts');
    const [lastUpdated, setLastUpdated] = useState(new Date());

    const fetchData = useCallback(async () => {
        try {
            const [detailedRes, alertsRes, usersRes] = await Promise.all([
                axios.get(`${API_BASE}/detailed-stats`),
                axios.get(`${API_BASE}/alerts`),
                axios.get(`${API_BASE}/top-users`)
            ]);

            const fullData = detailedRes.data;

            setStats(prev => {
                // Use the backend provided real-time throughput
                if (fullData.stats.throughput !== undefined) {
                    setCurrentThroughput(fullData.stats.throughput);
                }

                return {
                    ...fullData.stats,
                    distribution: fullData.distribution,
                    performance: fullData.performance,
                    system: fullData.system
                };
            });
            setAlerts(alertsRes.data);
            setTopUsers(usersRes.data);
            setLastUpdated(new Date());
        } catch (error) {
            console.error("Error fetching dashboard data", error);
        } finally {
            setLoading(false);
        }
    }, []);

    useEffect(() => {
        fetchData();
        const interval = setInterval(fetchData, 2000); // Polling every 2s
        return () => clearInterval(interval);
    }, [fetchData]);

    const fraudRate = useMemo(() => {
        return stats.totalEvents > 0 ? ((stats.fraudCount / stats.totalEvents) * 100).toFixed(2) : '0.00';
    }, [stats.totalEvents, stats.fraudCount]);

    const uptimeMinutes = useMemo(() => Math.floor(stats.performance.uptimeSeconds / 60), [stats.performance.uptimeSeconds]);

    if (loading && !stats.totalEvents) {
        return (
            <div className="min-h-screen flex items-center justify-center bg-slate-900">
                <div className="text-center">
                    <div className="w-10 h-10 mx-auto mb-4 border-t-2 border-blue-500 border-r-2 border-blue-500/30 rounded-full animate-spin"></div>
                    <p className="text-blue-400 text-xs font-bold tracking-[0.2em]">INITIALIZING SYSTEM</p>
                </div>
            </div>
        );
    }

    return (
        <div className="p-6 lg:p-10 max-w-[1920px] mx-auto space-y-8">
            {/* Header */}
            <header className="flex flex-col md:flex-row justify-between items-start md:items-end border-b border-slate-800 pb-6">
                <div>
                    <h1 className="text-3xl font-bold text-white tracking-tight">
                        <span className="text-transparent bg-clip-text bg-gradient-to-r from-blue-400 to-indigo-400">CloudScale</span> Event Intelligence
                    </h1>
                    <div className="flex items-center mt-3 space-x-6 text-xs font-medium text-slate-400">
                        <div className="flex items-center bg-slate-800/50 px-3 py-1 rounded-full border border-slate-700/50">
                            <span className={`w-2 h-2 rounded-full mr-2 shadow-[0_0_8px_rgba(0,0,0,0.5)] ${stats.system.apiStatus === 'Healthy' ? 'bg-emerald-500 shadow-emerald-500/50' : 'bg-rose-500 shadow-rose-500/50'}`}></span>
                            <span className="tracking-wide">SYSTEM STATUS: <span className="text-slate-200">{stats.system.apiStatus.toUpperCase()}</span></span>
                        </div>
                        <div className="bg-slate-800/50 px-3 py-1 rounded-full border border-slate-700/50">
                            UPTIME: <span className="text-slate-200 font-mono">{uptimeMinutes}m</span>
                        </div>
                    </div>
                </div>
                <div className="text-right mt-4 md:mt-0">
                    <div className="text-[10px] text-slate-500 uppercase tracking-wider font-bold mb-1">Live Sync</div>
                    <div className="font-mono text-sm text-blue-400 bg-blue-500/10 px-3 py-1 rounded border border-blue-500/20">
                        {lastUpdated.toLocaleTimeString()}
                    </div>
                </div>
            </header>

            {/* Row 1: Key Metrics (HUD) */}
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
                <StatsCard
                    title="Success Rate"
                    value={`${stats.successRate}%`}
                    subtext={stats.successRate < 99.9 ? '⚠️ Below Threshold' : 'Optimal Performance'}
                    indicatorColor={stats.successRate >= 99.9 ? "bg-emerald-500 shadow-emerald-500/50" : "bg-rose-500 shadow-rose-500/50"}
                />
                <StatsCard
                    title="Avg Latency"
                    value={`${stats.performance.avgLatencyMs}ms`}
                    subtext="Processing Time"
                    indicatorColor={stats.performance.avgLatencyMs < 50 ? "bg-blue-500 shadow-blue-500/50" : "bg-amber-500 shadow-amber-500/50"}
                />
                <StatsCard
                    title="Total Events"
                    value={stats.totalEvents.toLocaleString()}
                    subtext="Lifetime Volume"
                    indicatorColor="bg-indigo-500 shadow-indigo-500/50"
                />
                <StatsCard
                    title="Queue Depth"
                    value={stats.queueDepth?.toLocaleString() || '0'}
                    subtext={stats.queueDepth > 5000 ? 'Backpressure Detected' : 'No Backlog'}
                    indicatorColor={stats.queueDepth > 1000 ? "bg-amber-500 shadow-amber-500/50" : "bg-slate-400"}
                />
            </div>

            {/* Row 2: Charts Area (Now with Throughput Meter) */}
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
                <div className="lg:col-span-2 bg-slate-800/40 backdrop-blur-md p-6 rounded-xl border border-slate-700/50 h-[26rem] flex flex-col">
                    <h3 className="text-slate-400 text-xs font-bold uppercase tracking-widest mb-6 flex items-center">
                        <span className="w-1.5 h-4 bg-blue-500 rounded mr-2"></span>
                        System Throughput (Real-Time)
                    </h3>
                    <div className="flex-1 flex items-center justify-center">
                        {/* Radical Change: Gauge instead of Chart */}
                        <ThroughputMeter value={currentThroughput} max={stats.targetThroughput || 2000} />
                    </div>
                </div>
                <div className="bg-slate-800/40 backdrop-blur-md p-6 rounded-xl border border-slate-700/50 h-[26rem]">
                    <h3 className="text-slate-400 text-xs font-bold uppercase tracking-widest mb-6 flex items-center">
                        <span className="w-1.5 h-4 bg-indigo-500 rounded mr-2"></span>
                        Traffic Classification
                    </h3>
                    <div className="h-[calc(100%-2rem)] flex items-center justify-center">
                        <EventTypeBreakdown distribution={stats.distribution} />
                    </div>
                </div>
            </div>

            {/* Row 3: Actionable Data */}
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
                {/* Left: Alerts / Audit */}
                <div className="lg:col-span-2 space-y-4">
                    <div className="flex space-x-1 bg-slate-800/30 p-1 rounded-lg w-fit border border-slate-700/50">
                        <button
                            onClick={() => setActiveTab('alerts')}
                            className={`px-4 py-2 text-xs font-bold uppercase tracking-wide rounded-md transition-all ${activeTab === 'alerts'
                                ? 'bg-blue-600 text-white shadow-lg shadow-blue-500/20'
                                : 'text-slate-400 hover:text-white hover:bg-slate-700/50'
                                }`}
                        >
                            Live Security Alerts
                        </button>
                        <button
                            onClick={() => setActiveTab('audit')}
                            className={`px-4 py-2 text-xs font-bold uppercase tracking-wide rounded-md transition-all ${activeTab === 'audit'
                                ? 'bg-blue-600 text-white shadow-lg shadow-blue-500/20'
                                : 'text-slate-400 hover:text-white hover:bg-slate-700/50'
                                }`}
                        >
                            Database Verification
                        </button>
                    </div>
                    <div className="bg-slate-800/40 backdrop-blur-md rounded-xl border border-slate-700/50 overflow-hidden h-[450px]">
                        {activeTab === 'alerts' ? (
                            <RecentAlerts alerts={alerts} />
                        ) : (
                            <AuditLog />
                        )}
                    </div>
                </div>

                {/* Right: Leaderboard & Stats */}
                <div className="space-y-6">
                    <div className="bg-slate-800/40 backdrop-blur-md rounded-xl border border-slate-700/50 p-6 h-[450px] overflow-hidden flex flex-col">
                        <h3 className="text-slate-400 text-xs font-bold uppercase tracking-widest mb-4 flex items-center">
                            <span className="w-1.5 h-4 bg-rose-500 rounded mr-2"></span>
                            Risk Leaderboard
                        </h3>
                        <div className="flex-1 overflow-y-auto pr-2 custom-scrollbar">
                            <TopUsers users={topUsers} />
                        </div>
                    </div>
                </div>
            </div>

            {/* Mini Footer Stats */}
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                {[
                    { label: "API Replicas", value: stats.system.apiReplicas, color: "text-emerald-400" },
                    { label: "Processors", value: stats.system.processorReplicas, color: "text-indigo-400" },
                    { label: "Fraud Rate", value: `${fraudRate}%`, color: "text-rose-400" },
                    { label: "Concurrency", value: stats.system.maxConcurrent, color: "text-blue-400" }
                ].map((item, i) => (
                    <div key={i} className="bg-slate-800/30 p-4 rounded-lg border border-slate-700/30 flex flex-col items-center justify-center text-center">
                        <span className="text-[10px] text-slate-500 font-bold uppercase tracking-widest mb-1">{item.label}</span>
                        <span className={`text-xl font-mono font-bold ${item.color}`}>{item.value}</span>
                    </div>
                ))}
            </div>

            <footer className="text-center text-slate-600 text-[10px] pt-8 pb-4 uppercase tracking-[0.2em] font-medium">
                CloudScale Event Intelligence • Distributed Real-Time System • v1.0.1-[2026-01-27-19-28]
            </footer>
        </div>
    );
}
