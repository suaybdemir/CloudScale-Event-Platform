import requests
import time
import uuid

API_URL = "http://192.168.0.10:5000/api/events"
USER_ID = "victim_user_99"

def send_event(ip, ua, event_type="page_view", device_id=None):
    if not device_id:
        device_id = f"dev_{ip.replace('.', '_')}"
    
    payload = {
        "eventId": str(uuid.uuid4()),
        "eventType": event_type,
        "correlationId": str(uuid.uuid4()),
        "tenantId": "microsoft-internal",
        "userId": USER_ID,
        "createdAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "url": "/microsoft/products/ai-safety",
        "actionName": "security_scan",
        "metadata": {
            "ClientIp": ip
        }
    }
    headers = {
        "User-Agent": ua,
        "X-Device-Id": device_id
    }
    response = requests.post(API_URL, json=payload, headers=headers)
    print(f"Sent event [{event_type}] from {ip} -> Status: {response.status_code}")
    return response

print("--- Step 1: Establishing Baseline (Bypassing Learning Mode) ---")
for i in range(6):
    send_event("10.0.0.1", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0", device_id="standard_dev")
    time.sleep(0.1)

print("\n--- Step 2: Velocity Burst Simulation (Statistical Anomaly) ---")
for i in range(30):
    send_event("1.1.1.1", "Mozilla/5.0 (Linux; Android 10) Chrome/120.0.0.0 Mobile")

print("\n--- Step 3: Impossible Travel Simulation ---")
send_event("44.55.66.77", "Chrome on Windows", device_id="traveller_1")
time.sleep(0.5)
send_event("88.99.00.11", "Chrome on Windows", device_id="traveller_1")

print("\n--- Step 4: Device Pattern Anomaly ---")
send_event("10.0.0.1", "Firefox on Linux Mobile", device_id="standard_dev_BUT_NEW")

print("\n✅ Simulation Completed. Check Dashboard for 'Critical' and 'High' risk alerts.")
