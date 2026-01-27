import requests
import uuid
import time

API_URL = "http://192.168.0.10:5000/api/events"

def test_idempotency():
    print("\n--- Chaos Test 1: The Double-Tap (Idempotency) ---")
    event_id = str(uuid.uuid4())
    payload = {
        "eventId": event_id,
        "eventType": "page_view",
        "correlationId": "chaos_corr_1",
        "tenantId": "chaos-test",
        "userId": "chaos_user_1",
        "createdAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "url": "/chaos"
    }
    
    # First send
    r1 = requests.post(API_URL, json=payload)
    print(f"First Send ID {event_id}: Status {r1.status_code}")
    
    # Immediate second send with same ID
    r2 = requests.post(API_URL, json=payload)
    print(f"Double-Tap ID {event_id}: Status {r2.status_code}")
    print("Verification: If both are 202/200, system is handles idempotency gracefully.")

def test_poison_pill():
    print("\n--- Chaos Test 2: The Poison Pill (DLQ Handling) ---")
    
    # 1. Invalid JSON
    print("Sending Invalid JSON (Should be rejected by API or Dead-Lettered by Processor)...")
    try:
        r = requests.post(API_URL, data="NOT_JSON_AT_ALL", headers={"Content-Type": "application/json"})
        print(f"Status for bad raw data: {r.status_code}")
    except:
        print("API rejected invalid raw data as expected.")

    # 2. Unknown Event Type (Valid JSON but logically invalid)
    payload = {
        "eventId": str(uuid.uuid4()),
        "eventType": "UNKNOWN_MYSTERY_EVENT",
        "correlationId": "chaos_corr_2",
        "tenantId": "chaos-test"
    }
    r = requests.post(API_URL, json=payload)
    print(f"Status for unknown type: {r.status_code}")
    print("Verification: Check Ingestion API logs for 'Status 400' (Validation) or Processor logs for DLQ move.")

if __name__ == "__main__":
    test_idempotency()
    test_poison_pill()
