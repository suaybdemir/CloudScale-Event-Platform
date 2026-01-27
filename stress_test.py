import asyncio
import aiohttp
import time
import uuid
import random

API_URL = "http://192.168.0.10:5000/api/events"
TOTAL_EVENTS = 5000
CONCURRENCY = 50

async def send_event(session, i):
    event_type = random.choice(["page_view", "user_action", "purchase"])
    
    payload = {
        "eventId": str(uuid.uuid4()),
        "correlationId": str(uuid.uuid4()),
        "eventType": event_type,
        "tenantId": f"tenant-{random.randint(1, 5)}",
        "userId": f"user-{random.randint(1, 500)}",
        "metadata": {
            "IsLoadTest": "true",
            "Batch": "FinalStress"
        }
    }

    # Schema-specific fields
    if event_type == "page_view":
        payload["Url"] = f"/product/{random.randint(1, 100)}"
        payload["UserAgent"] = "StressTestBot/1.0"
    elif event_type == "user_action":
        payload["ActionName"] = random.choice(["click", "hover", "scroll"])
        payload["Properties"] = {"buttonId": "btn-123"}
    elif event_type == "purchase":
        payload["ActionName"] = "checkout_complete" # Inherited requirement!
        payload["Amount"] = random.uniform(10.0, 500.0)
        payload["Currency"] = "USD"
        payload["Items"] = [{"itemId": "item-1", "quantity": 1}]
    
    start_time = time.time()
    try:
        async with session.post(API_URL, json=payload, timeout=10) as response:
            latency = (time.time() - start_time) * 1000
            status = response.status
            return status, latency
    except Exception as e:
        return i, str(e)

async def stress_test():
    print(f"🔥 Starting Stress Test: {TOTAL_EVENTS} events with concurrency {CONCURRENCY}")
    print(f"Target: {API_URL}")
    print("-" * 60)
    
    async with aiohttp.ClientSession() as session:
        tasks = []
        for i in range(TOTAL_EVENTS):
            tasks.append(send_event(session, i))
            
            # Simple rate limiting for the injector
            if len(tasks) >= CONCURRENCY:
                results = await asyncio.gather(*tasks)
                tasks = []
                # Provide mid-test feedback
                if i % 500 == 0:
                     print(f"Progress: {i}/{TOTAL_EVENTS} events sent...")

        if tasks:
            results = await asyncio.gather(*tasks)

    # Analytics
    success_count = sum(1 for s, l in results if isinstance(s, int) and s in [202, 200])
    fail_count = TOTAL_EVENTS - success_count
    latencies = [l for s, l in results if isinstance(l, float)]
    
    avg_latency = sum(latencies) / len(latencies) if latencies else 0
    p95_latency = sorted(latencies)[int(len(latencies) * 0.95)] if latencies else 0
    
    print("-" * 60)
    print("🏆 Stress Test Completed!")
    print(f"Total Sent:  {TOTAL_EVENTS}")
    print(f"Success:     {success_count} ✅")
    print(f"Failed:      {fail_count} ❌")
    print(f"Avg Latency: {avg_latency:.2f}ms")
    print(f"P95 Latency: {p95_latency:.2f}ms")
    print("-" * 60)

if __name__ == "__main__":
    asyncio.run(stress_test())
