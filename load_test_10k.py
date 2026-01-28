import asyncio
import aiohttp
import time
import uuid
import json
from datetime import datetime

URL = "http://192.168.0.10:5000/api/ingest"
RPS = 10000  # 10k requests per second
DURATION = 30  # 30 seconds test
BATCH_SIZE = 1000  # Process in batches for better control

# Event templates for variety
EVENT_TEMPLATES = [
    {
        "type": "page_view",
        "url": "https://example.com/products",
    },
    {
        "type": "user_action",
        "actionName": "click_button",
        "actionValue": "add_to_cart"
    },
    {
        "type": "user_action",
        "actionName": "add_to_cart",
        "actionValue": "product_123"
    },
    {
        "type": "purchase",
        "actionName": "purchase",
        "actionValue": "100.50"
    }
]

async def send_event(session, user_id, tenant_id):
    """Send a single event"""
    template = EVENT_TEMPLATES[hash(user_id) % len(EVENT_TEMPLATES)]
    event = {
        **template,
        "specversion": "1.0",
        "id": str(uuid.uuid4()),
        "source": "/load/test",
        "tenantId": tenant_id,
        "correlationId": str(uuid.uuid4()),
        "userId": user_id
    }
    
    headers = {
        "X-Api-Key": "dev-secret-key",
        "Content-Type": "application/json"
    }
    
    try:
        async with session.post(URL, json=event, headers=headers, timeout=aiohttp.ClientTimeout(total=10)) as response:
            return {
                'status': response.status,
                'success': response.status == 202
            }
    except asyncio.TimeoutError:
        return {'status': 'timeout', 'success': False}
    except Exception as e:
        return {'status': f'error: {type(e).__name__}', 'success': False}

async def main():
    print(f"🚀 Starting EXTREME Load Test")
    print(f"📊 Target: {RPS:,} RPS for {DURATION} seconds")
    print(f"📦 Total Expected Requests: {RPS * DURATION:,}")
    print(f"⏰ Started at: {datetime.now().strftime('%H:%M:%S')}")
    print("-" * 60)
    
    # Connection pooling for high throughput
    connector = aiohttp.TCPConnector(
        limit=500,  # Max concurrent connections
        limit_per_host=500,
        ttl_dns_cache=300
    )
    
    timeout = aiohttp.ClientTimeout(total=10, connect=5)
    
    async with aiohttp.ClientSession(connector=connector, timeout=timeout) as session:
        start_time = time.time()
        all_tasks = []
        total_sent = 0
        
        # Generate diverse user IDs and tenant IDs
        tenants = [f"tenant-{i}" for i in range(10)]
        
        second = 0
        while time.time() - start_time < DURATION:
            second_start = time.time()
            second += 1
            
            # Create RPS tasks for this second
            batch_tasks = []
            for i in range(RPS):
                user_id = f"user-{uuid.uuid4().hex[:8]}"
                tenant_id = tenants[i % len(tenants)]
                task = asyncio.create_task(send_event(session, user_id, tenant_id))
                batch_tasks.append(task)
            
            all_tasks.extend(batch_tasks)
            total_sent += RPS
            
            # Print progress every second
            elapsed = time.time() - second_start
            print(f"⏱️  Second {second:2d} | Sent: {RPS:,} requests | Elapsed: {elapsed:.3f}s | Total: {total_sent:,}")
            
            # Wait for the remainder of the second
            wait_time = 1.0 - (time.time() - second_start)
            if wait_time > 0:
                await asyncio.sleep(wait_time)
        
        print("\n" + "=" * 60)
        print("📤 All requests sent! Waiting for responses...")
        print("=" * 60)
        
        # Gather all results
        results = await asyncio.gather(*all_tasks, return_exceptions=True)
        
        # Analyze results
        total_requests = len(results)
        success_count = sum(1 for r in results if isinstance(r, dict) and r.get('success'))
        failed_count = total_requests - success_count
        
        # Count status codes
        status_counts = {}
        for r in results:
            if isinstance(r, dict):
                status = r.get('status', 'unknown')
                status_counts[status] = status_counts.get(status, 0) + 1
        
        # Calculate metrics
        total_duration = time.time() - start_time
        actual_rps = total_requests / total_duration
        success_rate = (success_count / total_requests * 100) if total_requests > 0 else 0
        
        # Print results
        print("\n" + "🎯 " + "=" * 58)
        print("                    LOAD TEST RESULTS")
        print("=" * 60)
        print(f"⏱️  Total Duration:        {total_duration:.2f} seconds")
        print(f"📊 Total Requests:        {total_requests:,}")
        print(f"✅ Successful (202):      {success_count:,} ({success_rate:.2f}%)")
        print(f"❌ Failed:                {failed_count:,}")
        print(f"📈 Actual RPS:            {actual_rps:,.2f}")
        print(f"🎯 Target RPS:            {RPS:,}")
        print(f"📉 RPS Achievement:       {(actual_rps/RPS*100):.2f}%")
        print("\n📋 Status Code Distribution:")
        print("-" * 60)
        for status, count in sorted(status_counts.items(), key=lambda x: x[1], reverse=True):
            percentage = (count / total_requests * 100)
            print(f"   {str(status):20s}: {count:8,} ({percentage:6.2f}%)")
        
        print("=" * 60)
        
        # Performance assessment
        if success_rate >= 99:
            print("🏆 EXCELLENT! System handled the load perfectly!")
        elif success_rate >= 95:
            print("✅ GOOD! System performed well under load.")
        elif success_rate >= 80:
            print("⚠️  WARNING! System showed some stress.")
        else:
            print("❌ CRITICAL! System struggled under load.")
        
        print(f"\n⏰ Finished at: {datetime.now().strftime('%H:%M:%S')}")

if __name__ == "__main__":
    print("\n" + "=" * 60)
    print("  CloudScale Event Intelligence Platform - Load Test")
    print("=" * 60 + "\n")
    
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\n\n⚠️  Test interrupted by user")
    except Exception as e:
        print(f"\n\n❌ Test failed with error: {e}")
