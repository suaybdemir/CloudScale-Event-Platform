import requests
import uuid
import time
from datetime import datetime, timedelta, timezone

API_URL = "http://192.168.0.10:5000/api/events"

def send_event(payload):
    try:
        response = requests.post(API_URL, json=payload)
        print(f"Status: {response.status_code} | Body: {response.text[:100]}")
        return response
    except Exception as e:
        print(f"Error: {e}")
        return None

def run_tests():
    print("🚀 Starting LIVE Principal Safeguard Tests...")
    print("-" * 60)

    # 1. Idempotency Collision Test
    shared_id = str(uuid.uuid4())
    print(f"\n[Test 1] Idempotency Collision (ID: {shared_id})")
    
    print("Sending Original Event...")
    send_event({
        "eventId": shared_id,
        "eventType": "page_view",
        "tenantId": "verification-tenant",
        "correlationId": "corr-1",
        "userId": "principal-tester",
        "url": "/original"
    })
    
    print("Waiting for persistence...")
    time.sleep(3)
    
    print("Sending TAMPERED Event (Same ID, Different Content)...")
    send_event({
        "eventId": shared_id,
        "eventType": "page_view",
        "tenantId": "verification-tenant",
        "correlationId": "TAMPERED",
        "userId": "hacker-123", # Changed content!
        "url": "/tampered"
    })

    # 2. Temporal Integrity Test
    print("\n[Test 2] Temporal Integrity (Late Arrival)")
    late_time = (datetime.now(timezone.utc) - timedelta(minutes=15)).isoformat()
    print(f"Sending event with late timestamp: {late_time}")
    send_event({
        "eventId": str(uuid.uuid4()),
        "eventType": "page_view",
        "tenantId": "verification-tenant",
        "correlationId": "corr-late",
        "userId": "late-user",
        "createdAt": late_time
    })

    # 3. Stats SLA Test
    print("\n[Test 3] System SLA (Stats API Latency)")
    STATS_URL = "http://192.168.0.10:5000/api/dashboard/detailed-stats"
    start_time = time.time()
    try:
        resp = requests.get(STATS_URL, timeout=5)
        latency = (time.time() - start_time) * 1000
        print(f"Stats API Latency: {latency:.2f}ms")
        if latency < 50:
            print("✅ SLA Verified: Response below 50ms.")
        else:
            print("⚠️ SLA Warning: Response exceeded 50ms.")
        
        data = resp.json()
        print(f"Current System State: Events={data['stats']['totalEvents']}, Fraud={data['stats']['fraudCount']}, Replicas={data['system']['apiReplicas']}")
    except Exception as e:
        print(f"Error checking stats: {e}")

    # 4. Forced Suspicious Test (Manual trigger for verification)
    print("\n[Test 4] Force Suspicious Alert (Persistence-First Check)")
    send_event({
        "eventId": str(uuid.uuid4()),
        "eventType": "page_view",
        "tenantId": "verification-tenant",
        "correlationId": "force-alert-1",
        "userId": "manual-attacker",
        "metadata": { "ForceSuspicious": "true" }
    })

    print("\n" + "=" * 60)
    print("✅ Comprehensive Verification Sent.")
    print("Now run 'python diagnose_remote.py' to verify rehydration & order!")
    print("=" * 60)

if __name__ == "__main__":
    run_tests()
