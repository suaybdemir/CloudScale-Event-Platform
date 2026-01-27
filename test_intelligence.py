import asyncio
import aiohttp
import random
from datetime import datetime

URL = "http://localhost:5000/api/events"

async def send_event(session, event_type, action_name=None, user_id=None, client_ip=None):
    event = {
        "eventType": event_type,
        "tenantId": "test-tenant",
        "correlationId": f"{datetime.now().timestamp()}",
        "userId": user_id or f"user-{random.randint(1, 10)}",
        "metadata": {}
    }
    
    if client_ip:
        event["metadata"]["ClientIp"] = client_ip
    
    if action_name:
        event["actionName"] = action_name
    
    if event_type == "page_view":
        event["url"] = f"https://example.com/{random.choice(['home', 'products', 'about', 'contact'])}"
    
    async with session.post(URL, json=event) as response:
        status = response.status
        return (event_type, action_name or '', user_id, status)

async def main():
    async with aiohttp.ClientSession() as session:
        
        print("=" * 60)
        print("EXTENDED INTELLIGENCE TEST - CLOUDSCALE PLATFORM")
        print("=" * 60)
        
        # Test 1: Fraud Detection - High Velocity Attack
        print("\n🔴 TEST 1: Fraud Detection (50 rapid requests from same IP)")
        print("-" * 60)
        tasks = []
        attacker_ip = "192.168.1.100"
        for i in range(50):
            tasks.append(send_event(session, "page_view", client_ip=attacker_ip, user_id="attacker-bot"))
        
        results = await asyncio.gather(*tasks)
        accepted = sum(1 for r in results if r[3] == 202)
        print(f"✅ Sent: 50 events | Accepted: {accepted}")
        print(f"⚠️  Fraud detection should trigger after 10th request")
        
        await asyncio.sleep(1)
        
        # Test 2: Normal User Activity (Build Leaderboard)
        print("\n📊 TEST 2: Normal User Activity (Building Leaderboard)")
        print("-" * 60)
        tasks = []
        
        # User 1: Power user (lots of activity)
        for _ in range(20):
            tasks.append(send_event(session, "page_view", user_id="alice-2024"))
        for _ in range(5):
            tasks.append(send_event(session, "user_action", action_name="like_post", user_id="alice-2024"))
        for _ in range(2):
            tasks.append(send_event(session, "purchase", action_name="checkout", user_id="alice-2024"))
        
        # User 2: Medium activity
        for _ in range(15):
            tasks.append(send_event(session, "page_view", user_id="bob-smith"))
        for _ in range(3):
            tasks.append(send_event(session, "user_action", action_name="share", user_id="bob-smith"))
        tasks.append(send_event(session, "purchase", action_name="buy", user_id="bob-smith"))
        
        # User 3: Light activity
        for _ in range(10):
            tasks.append(send_event(session, "page_view", user_id="charlie-dev"))
        for _ in range(2):
            tasks.append(send_event(session, "user_action", action_name="comment", user_id="charlie-dev"))
        
        # Users 4-10: Random activity
        for uid in range(4, 11):
            user_id = f"user-{uid}"
            for _ in range(random.randint(3, 12)):
                tasks.append(send_event(session, "page_view", user_id=user_id))
            for _ in range(random.randint(0, 3)):
                tasks.append(send_event(session, "user_action", action_name="click", user_id=user_id))
        
        results = await asyncio.gather(*tasks)
        print(f"✅ Sent {len(results)} events from 10 different users")
        
        await asyncio.sleep(1)
        
        # Test 3: Cart Abandonment Scenario
        print("\n🛒 TEST 3: Cart Abandonment Detection")
        print("-" * 60)
        
        # User adds to cart but doesn't purchase
        result = await send_event(session, "user_action", action_name="add_to_cart", user_id="abandoner-123")
        print(f"✅ Sent 'add_to_cart' event: {result[3]}")
        print("⏳ Waiting 1 minute for scheduled check_cart_status message...")
        print("   (Check Event Processor logs for 'Cart Abandonment Detected')")
        
        await asyncio.sleep(2)
        
        # Test 4: Mixed Traffic (Realistic Load)
        print("\n🌐 TEST 4: Mixed Traffic Simulation (100 events)")
        print("-" * 60)
        tasks = []
        event_types = ["page_view"] * 60 + ["user_action"] * 30 + ["purchase"] * 10
        
        for _ in range(100):
            event_type = random.choice(event_types)
            user_id = f"user-{random.randint(1, 20)}"
            client_ip = f"10.0.{random.randint(1, 5)}.{random.randint(1, 255)}"
            
            if event_type == "user_action":
                action = random.choice(["click", "like", "share", "comment"])
                tasks.append(send_event(session, event_type, action_name=action, user_id=user_id, client_ip=client_ip))
            elif event_type == "purchase":
                tasks.append(send_event(session, event_type, action_name="buy", user_id=user_id, client_ip=client_ip))
            else:
                tasks.append(send_event(session, event_type, user_id=user_id, client_ip=client_ip))
        
        results = await asyncio.gather(*tasks)
        print(f"✅ Sent {len(results)} mixed events")
        
        print("\n" + "=" * 60)
        print("✅ TEST COMPLETED!")
        print("=" * 60)
        print("\n📊 Dashboard should now show:")
        print("   - Total Events: 200+")
        print("   - Fraud Alerts: 40+ (from rapid attack)")
        print("   - Top Users: alice-2024, bob-smith, charlie-dev, etc.")
        print("\n🌐 Open Dashboard: http://localhost:5173")
        print("=" * 60)

if __name__ == "__main__":
    asyncio.run(main())
