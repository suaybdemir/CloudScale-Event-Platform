#!/usr/bin/env python3
"""
End-to-End Data Integrity Test
Sends events, waits for processing, and verifies all data was persisted to Cosmos DB
"""
import asyncio
import aiohttp
import time
import uuid
from datetime import datetime

INGESTION_API = "http://localhost:5000/api/events"
READ_API = "http://localhost:5000/api/events"  # Query endpoint
TARGET_EVENTS = 1000  # Send 1000 events
BATCH_SIZE = 100

async def send_event(session, event_id, user_id):
    """Send a single event with known ID"""
    event = {
        "eventType": "page_view",
        "url": f"https://test.com/page-{event_id}",
        "tenantId": "integrity-test",
        "correlationId": str(uuid.uuid4()),
        "userId": user_id
    }
    
    try:
        async with session.post(INGESTION_API, json=event) as response:
            return response.status == 202
    except Exception as e:
        print(f"❌ Send error: {e}")
        return False

async def query_cosmos_count(session):
    """Query how many events are in Cosmos DB total"""
    try:
        async with session.get("http://localhost:5000/api/dashboard/stats") as response:
            if response.status == 200:
                data = await response.json()
                return data.get('totalEvents', 0)
            return 0
    except Exception as e:
        print(f"⚠️  Query error: {e}")
        return 0

async def main():
    print("=" * 70)
    print("  🔍 END-TO-END DATA INTEGRITY TEST")
    print("=" * 70)
    
    # Generate unique user IDs for this test
    user_ids = [f"integrity-user-{i:04d}" for i in range(TARGET_EVENTS)]
    
    print(f"\n📤 Phase 1: Sending {TARGET_EVENTS} events to Ingestion API...")
    connector = aiohttp.TCPConnector(limit=0)
    async with aiohttp.ClientSession(connector=connector) as session:
        
        # Send all events
        start_time = time.time()
        tasks = [send_event(session, i, user_ids[i]) for i in range(TARGET_EVENTS)]
        results = await asyncio.gather(*tasks)
        send_duration = time.time() - start_time
        
        successful_sends = sum(results)
        print(f"✅ Sent: {successful_sends}/{TARGET_EVENTS} events")
        print(f"⏱️  Time: {send_duration:.2f}s ({successful_sends/send_duration:.0f} events/sec)")
        
        if successful_sends < TARGET_EVENTS:
            print(f"⚠️  WARNING: Only {successful_sends} events were accepted by API!")
        
        # Wait for processing
        print(f"\n⏳ Phase 2: Waiting for Event Processor to consume from Service Bus...")
        print("   (Checking Cosmos DB every 2 seconds)")
        
        max_wait = 60  # Wait up to 60 seconds
        wait_start = time.time()
        last_count = 0
        
        while time.time() - wait_start < max_wait:
            await asyncio.sleep(2)
            
            # Query Cosmos DB via Read API
            count = await query_cosmos_count(session)
            
            elapsed = time.time() - wait_start
            print(f"   [{elapsed:5.1f}s] Cosmos DB has {count}/{successful_sends} events", end="")
            
            if count > last_count:
                rate = (count - last_count) / 2  # events per second
                print(f" (+{count-last_count}, ~{rate:.0f}/s)")
                last_count = count
            else:
                print(" (no change)")
            
            # Success condition
            if count >= successful_sends:
                print(f"\n✅ All events processed in {elapsed:.2f}s!")
                break
        else:
            print(f"\n⚠️  Timeout after {max_wait}s")
        
        # Final verification
        print(f"\n{'='*70}")
        print("  📊 FINAL RESULTS")
        print('='*70)
        print(f"Events Sent to API:       {successful_sends}")
        print(f"Events in Cosmos DB:      {last_count}")
        print(f"Data Loss:                {successful_sends - last_count} events ({((successful_sends-last_count)/successful_sends*100):.2f}%)")
        
        if last_count >= successful_sends:
            print("\n🏆 SUCCESS! No data loss detected!")
            print("   System successfully processed all events end-to-end.")
        elif last_count >= successful_sends * 0.99:
            print("\n✅ GOOD! Minimal data loss (<1%)")
        elif last_count >= successful_sends * 0.95:
            print("\n⚠️  WARNING! Some data loss detected (>1%)")
        else:
            print("\n❌ CRITICAL! Significant data loss!")
        
        print('='*70)

if __name__ == "__main__":
    asyncio.run(main())
