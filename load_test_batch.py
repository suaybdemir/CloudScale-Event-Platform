import asyncio
import aiohttp
import time
import uuid
import random
from datetime import datetime, timezone
import sys

# Configuration
API_URL = "http://localhost:5000/api/events/batch"
TOTAL_REQUESTS = 10000  # Number of batch requests
BATCH_SIZE = 200       # Events per batch
CONCURRENCY = 100       # Concurrent batch requests

print(f"🚄 BATCH LOAD TEST")
print(f"Batches: {TOTAL_REQUESTS} x {BATCH_SIZE} events = {TOTAL_REQUESTS * BATCH_SIZE} total events")
print(f"Concurrency: {CONCURRENCY}")

def generate_event():
    return {
        "eventId": str(uuid.uuid4()),
        "eventType": "page_view",
        "correlationId": f"batch-{uuid.uuid4()}",
        "tenantId": "tenant-batch",
        "Url": f"/product/{random.randint(1, 1000)}",
        "userId": f"user-{random.randint(1, 1000)}",
        "createdAt": datetime.now(timezone.utc).isoformat(),
        "metadata": {
            "ClientIp": f"10.{random.randint(1,255)}.{random.randint(1,255)}.{random.randint(1,255)}",
            "UserAgent": "Python-Batch/1.0",
            # Inject fraud signal for verification
            "ForceSuspicious": "true" if random.random() < 0.05 else "false"
        }
    }

async def send_batch(session):
    batch = [generate_event() for _ in range(BATCH_SIZE)]
    
    timeout = aiohttp.ClientTimeout(total=5)
    try:
        async with session.post(API_URL, json=batch, timeout=timeout) as response:
            await response.read()
            return response.status
    except Exception as e:
        print(f"Error: {e}")
        return 0

async def worker(queue, session, stats):
    while True:
        try:
            _ = queue.get_nowait()
        except asyncio.QueueEmpty:
            break

        status = await send_batch(session)
        stats['completed'] += 1
        
        if status == 202:
            stats['success'] += 1
        else:
            stats['fail'] += 1
        
        queue.task_done()

async def main():
    start_time = time.time()
    queue = asyncio.Queue()
    stats = {'success': 0, 'fail': 0, 'completed': 0}
    
    for _ in range(TOTAL_REQUESTS):
        queue.put_nowait(1)
        
    conn = aiohttp.TCPConnector(limit=CONCURRENCY)
    async with aiohttp.ClientSession(connector=conn) as session:
        workers = [asyncio.create_task(worker(queue, session, stats)) for _ in range(CONCURRENCY)]
        
        while not queue.empty():
            elapsed = time.time() - start_time
            events_processed = stats['completed'] * BATCH_SIZE
            rps = events_processed / elapsed if elapsed > 0 else 0
            sys.stdout.write(f"\r📊 Speed: {rps:.0f} events/s | Batches: {stats['completed']}/{TOTAL_REQUESTS}")
            sys.stdout.flush()
            await asyncio.sleep(0.5)
            
        await queue.join()
        for w in workers: w.cancel()

    duration = time.time() - start_time
    total_events = TOTAL_REQUESTS * BATCH_SIZE
    rps = total_events / duration
    
    print("\n\n✅ BATCH TEST FINISHED")
    print(f"   Duration:   {duration:.2f}s")
    print(f"   Total Events: {total_events}")
    print(f"   Throughput: {rps:.0f} events/sec")
    print(f"   Success Rate: {(stats['success']/TOTAL_REQUESTS)*100:.1f}%")

if __name__ == "__main__":
    try:
        import aiohttp
    except ImportError:
         import subprocess
         subprocess.check_call([sys.executable, "-m", "pip", "install", "aiohttp"])
         import aiohttp
    asyncio.run(main())
