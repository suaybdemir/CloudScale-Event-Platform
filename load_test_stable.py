import asyncio
import aiohttp
import time
import uuid
import random
from datetime import datetime, timezone
import sys
from collections import Counter

# Configuration
API_URL = "http://localhost:5000/api/events"
TOTAL_REQUESTS = 10000 
CONCURRENCY = 50 # Safe limit for Emulator
BATCH_SIZE = 1000

print(f"🛡️ STABLE LOAD TEST: {TOTAL_REQUESTS} requests | {CONCURRENCY} workers")

async def send_event(session):
    payload = {
        "eventId": str(uuid.uuid4()),
        "eventType": "page_view",
        "correlationId": f"stable-{uuid.uuid4()}",
        "tenantId": "tenant-stable",
        "url": f"/product/stable/{random.randint(1, 100)}",
        "userId": f"user-{random.randint(1, 1000)}",
        "createdAt": datetime.now(timezone.utc).isoformat(),
        "metadata": {
            "ClientIp": f"10.{random.randint(1,255)}.{random.randint(1,255)}.{random.randint(1,255)}",
            "UserAgent": "Python-Stable/1.0"
        }
    }
    
    try:
        async with session.post(API_URL, json=payload) as response:
            await response.read()
            return response.status
    except Exception:
        return 0

async def worker(queue, session, stats, error_codes):
    while True:
        try:
            _ = queue.get_nowait()
        except asyncio.QueueEmpty:
            break

        status = await send_event(session)
        stats['completed'] += 1
        
        if status == 202 or status == 200:
            stats['success'] += 1
        elif status == 0:
            stats['conn_error'] += 1
        else:
            stats['fail'] += 1
            # Track error codes (thread-safe enough for simple Counter)
            error_codes[status] += 1
            
        # Breath for Emulator
        await asyncio.sleep(0.01) 
        queue.task_done()

async def main():
    start_time = time.time()
    queue = asyncio.Queue()
    stats = {'success': 0, 'fail': 0, 'conn_error': 0, 'completed': 0}
    error_codes = Counter()
    
    for _ in range(TOTAL_REQUESTS):
        queue.put_nowait(1)
        
    conn = aiohttp.TCPConnector(limit=CONCURRENCY)
    async with aiohttp.ClientSession(connector=conn) as session:
        workers = [asyncio.create_task(worker(queue, session, stats, error_codes)) for _ in range(CONCURRENCY)]
        
        while not queue.empty():
            elapsed = time.time() - start_time
            rps = stats['completed'] / elapsed if elapsed > 0 else 0
            sys.stdout.write(f"\r📊 Speed: {rps:.0f} req/s | OK: {stats['success']} | Err: {stats['fail']} (Codes: {dict(error_codes)})")
            sys.stdout.flush()
            await asyncio.sleep(0.5)
            
        await queue.join()
        for w in workers: w.cancel()

    duration = time.time() - start_time
    rps = TOTAL_REQUESTS / duration
    
    print("\n\n✅ STABLE TEST FINISHED")
    print(f"   Duration:   {duration:.2f}s")
    print(f"   Throughput: {rps:.2f} req/sec")
    print(f"   Success:    {stats['success']} ({(stats['success']/TOTAL_REQUESTS)*100:.1f}%)")
    print(f"   Error Codes: {dict(error_codes)}")

if __name__ == "__main__":
    try:
        import aiohttp
    except ImportError:
         import subprocess
         subprocess.check_call([sys.executable, "-m", "pip", "install", "aiohttp"])
         import aiohttp
    asyncio.run(main())
