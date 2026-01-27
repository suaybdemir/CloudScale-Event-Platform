import asyncio
import aiohttp
import time
import uuid
import json
from datetime import datetime

URL = "http://localhost:5000/api/events"
RPS = 10  # Reduced to 10 for connectivity check
DURATION = 5  # 5 seconds
BATCH_SIZE = 1000

# Event templates for variety
EVENT_TEMPLATES = [
    {
        "eventType": "page_view",
        "url": "https://example.com/products",
    },
    {
        "eventType": "user_action",
        "actionName": "click_button",
        "actionValue": "add_to_cart"
    },
    {
        "eventType": "user_action",
        "actionName": "add_to_cart",
        "actionValue": "product_123"
    },
    {
        "eventType": "purchase",
        "actionName": "purchase",
        "actionValue": "100.50"
    }
]

async def send_event(session, user_id, tenant_id):
    """Send a single event"""
    template = EVENT_TEMPLATES[hash(user_id) % len(EVENT_TEMPLATES)]
    event = {
        **template,
        "tenantId": tenant_id,
        "correlationId": str(uuid.uuid4()),
        "userId": user_id
    }
    
    try:
        async with session.post(URL, json=event, timeout=aiohttp.ClientTimeout(total=10)) as response:
            return {
                'status': response.status,
                'success': response.status == 202
            }
    except asyncio.TimeoutError:
        return {'status': 'timeout', 'success': False}
    except Exception as e:
        return {'status': f'error: {type(e).__name__}', 'success': False}

async def main():
    print(f"🚀 Starting Connectivity Test")
    print(f"📊 Target: {RPS:,} RPS for {DURATION} seconds")
    
    connector = aiohttp.TCPConnector(limit=50)
    timeout = aiohttp.ClientTimeout(total=10)
    
    async with aiohttp.ClientSession(connector=connector, timeout=timeout) as session:
        start_time = time.time()
        tasks = []
        
        for i in range(DURATION):
            print(f"Sending batch {i+1}...")
            for _ in range(RPS):
                user_id = f"user-{uuid.uuid4().hex[:8]}"
                tenant_id = "tenant-1"
                tasks.append(asyncio.create_task(send_event(session, user_id, tenant_id)))
            await asyncio.sleep(1)
            
        print("Waiting for responses...")
        results = await asyncio.gather(*tasks)
        
        success = sum(1 for r in results if r.get('success'))
        total = len(results)
        
        print(f"\nResults: {success}/{total} Successful ({success/total*100:.1f}%)")
        if success == total:
            print("✅ Connectivity Verified!")
        else:
            print("❌ Connectivity Issues Detected")
            print(f"Sample errors: {[r for r in results if not r.get('success')][:5]}")

if __name__ == "__main__":
    asyncio.run(main())
