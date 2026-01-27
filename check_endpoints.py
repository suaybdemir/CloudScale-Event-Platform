import paramiko
import sys
import time
import subprocess

API_BASE = "http://192.168.0.10:5000/api/dashboard"

def check_endpoint(name, url):
    print(f"\n--- Checking {name} ({url}) ---")
    start = time.time()
    try:
        # Use curl with max-time to detect hangs
        cmd = ["curl", "-v", "--max-time", "5", url]
        result = subprocess.run(cmd, capture_output=True, text=True)
        duration = time.time() - start
        
        print(f"Status: {result.returncode}")
        print(f"Duration: {duration:.2f}s")
        if result.returncode == 0:
            print("Response (Head):", result.stdout[:200])
        else:
            print("Error:", result.stderr)
            
    except Exception as e:
        print(f"Exception: {e}")

def diagnose():
    check_endpoint("Stats", f"{API_BASE}/stats")
    check_endpoint("Alerts", f"{API_BASE}/alerts")
    check_endpoint("Top Users", f"{API_BASE}/top-users")

if __name__ == "__main__":
    diagnose()
