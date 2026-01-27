#!/usr/bin/env python3
"""
True End-to-End Data Integrity Test
Measures baseline, sends events, verifies exact count increase
"""
import asyncio
import aiohttp
import time
import uuid

INGESTION_API = "http://localhost:5000/api/events"
STATS_API = "http://localhost:5000/api/dashboard/stats"
TARGET_EVENTS = 5000  # 5k events

async def get_cosmos_count(session):
    """Get current total event count from Cosmos DB"""
    async with session.get(STATS_API) as response:
        if response.status == 200:
            data = await response.json()
            return data.get('totalEvents', 0)
    return 0

async def send_event(session, idx):
    """Send a single event"""
    event = {
        "eventType": "page_view",
        "url": f"https://e2e-test.com/page-{idx}",
        "tenantId": "e2e-test",
        "correlationId": str(uuid.uuid4()),
        "userId": f"e2e-user-{idx:05d}"
    }
    try:
        async with session.post(INGESTION_API, json=event) as response:
            return response.status == 202
    except:
        return False

async def main():
    print("=" * 70)
    print("  🔬 TRUE END-TO-END DATA INTEGRITY TEST")
    print("=" * 70)
    
    connector = aiohttp.TCPConnector(limit=0)
    async with aiohttp.ClientSession(connector=connector) as session:
        
        # 1. Get baseline
        baseline = await get_cosmos_count(session)
        print(f"\n📊 Baseline Events in Cosmos DB: {baseline}")
        
        # 2. Send events
        print(f"\n📤 Sending {TARGET_EVENTS} events...")
        start = time.time()
        
        tasks = [send_event(session, i) for i in range(TARGET_EVENTS)]
        results = await asyncio.gather(*tasks)
        
        sent_count = sum(results)
        send_time = time.time() - start
        print(f"✅ API Accepted: {sent_count}/{TARGET_EVENTS} ({sent_count/send_time:.0f}/sec)")
        
        # 3. Wait for processing
        print(f"\n⏳ Waiting for Event Processor...")
        target = baseline + sent_count
        
        for i in range(30):  # Max 60 seconds
            await asyncio.sleep(2)
            current = await get_cosmos_count(session)
            new_events = current - baseline
            
            progress = (new_events / sent_count * 100) if sent_count > 0 else 0
            print(f"   [{(i+1)*2:2d}s] Processed: {new_events}/{sent_count} ({progress:.1f}%)")
            
            if current >= target:
                break
        
        # 4. Final result
        final_count = await get_cosmos_count(session)
        processed = final_count - baseline
        
        print("\n" + "=" * 70)
        print("  📊 FINAL RESULTS")
        print("=" * 70)
        print(f"Events Sent:      {sent_count}")
        print(f"Events Processed: {processed}")
        print(f"Data Loss:        {sent_count - processed} ({(1 - processed/sent_count)*100:.2f}%)" if sent_count > 0 else "N/A")
        
        if processed >= sent_count:
            print("\n🏆 SUCCESS! Zero data loss - all events persisted!")
        elif processed >= sent_count * 0.99:
            print("\n✅ GOOD! Less than 1% data loss")
        else:
            print("\n❌ WARNING! Significant data loss detected")

if __name__ == "__main__":
    asyncio.run(main())
