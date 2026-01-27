# CloudScale Dashboard

Real-time monitoring dashboard for the CloudScale Event Intelligence Platform.

## Technology Stack

- **React 19** - UI framework
- **Vite 7** - Build tool
- **Tailwind CSS 4** - Styling
- **Chart.js** - Data visualization
- **Axios** - API client

## Features

### Real-time Metrics
- **Total Events** - Lifetime event count
- **Fraud Detection** - Suspicious activity counter
- **Queue Depth** - Service Bus backpressure indicator
- **SLO Status** - Error budget tracking

### Visualizations
- **Live Traffic Chart** - Events per second stream
- **Event Distribution** - Breakdown by event type
- **System Health** - Component status overview
- **Recent Alerts** - Fraud detection notifications

### Components

| Component | Purpose |
|-----------|---------|
| `StatsCard` | Gradient metric cards |
| `RealTimeChart` | Animated line chart |
| `RecentAlerts` | Alert list with severity |
| `TopUsers` | Most active users |
| `SystemHealth` | SRE-style health indicators |
| `EventTypeBreakdown` | Event type distribution |

## Quick Start

```bash
# Install dependencies
npm install

# Development server
npm run dev

# Production build
npm run build
```

## Configuration

Set API endpoint via environment variable:

```bash
VITE_API_URL=http://localhost:5000/api/dashboard
```

## API Endpoints Required

The dashboard expects these endpoints from the backend:

| Endpoint | Response |
|----------|----------|
| `GET /stats` | `{ totalEvents, fraudCount, queueDepth }` |
| `GET /alerts` | `[{ id, message, severity, timestamp }]` |
| `GET /top-users` | `[{ userId, eventCount, score }]` |

## Screenshots

Dashboard provides:
- Dark mode glassmorphism design
- Responsive grid layout
- Ambient glow effects
- Live data polling (2s intervals)

## Build for Production

```bash
npm run build
# Output in dist/
```

## Docker

```bash
docker build -t cloudscale-dashboard .
docker run -p 5173:5173 cloudscale-dashboard
```
