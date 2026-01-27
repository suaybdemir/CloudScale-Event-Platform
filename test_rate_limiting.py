#!/usr/bin/env python3
"""
Rate Limiting Load Test for CloudScale Platform

Tests the Token Bucket + Sliding Window rate limiting implementation.
Verifies 429 responses after limit exceeded.
"""

import asyncio
import aiohttp
import time
from dataclasses import dataclass
from typing import List

import os

# Configuration
API_URL = os.getenv("API_URL", "http://localhost:5000/api/events")
SINGLE_IP_BURST = 150  # Exceed 100 token limit
GLOBAL_BURST = 12000   # Exceed 10k/min limit

@dataclass
class TestResult:
    total_requests: int
    success_count: int
    rate_limited_count: int
    error_count: int
    duration_sec: float
    
    @property
    def success_rate(self) -> float:
        return (self.success_count / self.total_requests) * 100 if self.total_requests > 0 else 0
    
    @property
    def rate_limited_rate(self) -> float:
        return (self.rate_limited_count / self.total_requests) * 100 if self.total_requests > 0 else 0


def create_event(i: int) -> dict:
    return {
        "eventType": "page_view",
        "correlationId": f"test-ratelimit-{i}",
        "tenantId": "test-tenant",
        "userId": f"test-user-{i}",
        "url": f"/test/page/{i}",
        "metadata": {"test": "true"}
    }


async def send_request(session: aiohttp.ClientSession, event: dict, custom_ip: str = None) -> int:
    """Send a single request and return status code."""
    headers = {"Content-Type": "application/json"}
    if custom_ip:
        headers["X-Forwarded-For"] = custom_ip
    
    try:
        async with session.post(API_URL, json=event, headers=headers, timeout=10) as response:
            if response.status not in [200, 202, 429]:
                 print(f"Error {response.status}: {await response.text()}")
            return response.status
    except Exception as e:
        print(f"Request error: {e}")
        return 0


async def test_single_ip_rate_limit() -> TestResult:
    """
    Test 1: Single IP Rate Limiting (Token Bucket)
    Send 150 requests from same IP rapidly.
    Expected: ~100 success (202), ~50 rate limited (429)
    """
    print("\n" + "="*60)
    print("TEST 1: Single IP Rate Limiting (Token Bucket)")
    print("="*60)
    print(f"Sending {SINGLE_IP_BURST} requests from single IP...")
    
    start = time.time()
    success = 0
    rate_limited = 0
    errors = 0
    
    async with aiohttp.ClientSession() as session:
        tasks = [
            send_request(session, create_event(i), custom_ip="192.168.1.100")
            for i in range(SINGLE_IP_BURST)
        ]
        results = await asyncio.gather(*tasks)
        
        for status in results:
            if status == 202:
                success += 1
            elif status == 429:
                rate_limited += 1
            else:
                errors += 1
    
    duration = time.time() - start
    result = TestResult(SINGLE_IP_BURST, success, rate_limited, errors, duration)
    
    print(f"\nResults:")
    print(f"  ✅ Success (202): {success} ({result.success_rate:.1f}%)")
    print(f"  🚫 Rate Limited (429): {rate_limited} ({result.rate_limited_rate:.1f}%)")
    print(f"  ❌ Errors: {errors}")
    print(f"  ⏱️ Duration: {duration:.2f}s")
    
    # Validate
    if rate_limited >= 40:  # Expect at least 40 rejections
        print("  ✅ TEST PASSED: Rate limiting working correctly")
    else:
        print("  ❌ TEST FAILED: Expected more rate limit rejections")
    
    return result


async def test_global_rate_limit() -> TestResult:
    """
    Test 2: Global Rate Limiting (Sliding Window)
    Send 12,000 requests from different IPs rapidly.
    Expected: ~10,000 success, ~2,000 rate limited
    """
    print("\n" + "="*60)
    print("TEST 2: Global Rate Limiting (Sliding Window)")
    print("="*60)
    print(f"Sending {GLOBAL_BURST} requests from multiple IPs...")
    
    start = time.time()
    success = 0
    rate_limited = 0
    errors = 0
    
    # Use different IPs to bypass per-IP limit
    async with aiohttp.ClientSession() as session:
        tasks = []
        for i in range(GLOBAL_BURST):
            # Rotate through 1000 different IPs
            ip = f"10.0.{i % 256}.{(i // 256) % 256}"
            tasks.append(send_request(session, create_event(i), custom_ip=ip))
        
        # Process in batches to avoid overwhelming
        batch_size = 500
        for batch_start in range(0, len(tasks), batch_size):
            batch = tasks[batch_start:batch_start + batch_size]
            results = await asyncio.gather(*batch)
            
            for status in results:
                if status == 202:
                    success += 1
                elif status == 429:
                    rate_limited += 1
                else:
                    errors += 1
            
            # Progress indicator
            print(f"  Progress: {batch_start + len(batch)}/{GLOBAL_BURST}", end="\r")
    
    duration = time.time() - start
    result = TestResult(GLOBAL_BURST, success, rate_limited, errors, duration)
    
    print(f"\nResults:")
    print(f"  ✅ Success (202): {success} ({result.success_rate:.1f}%)")
    print(f"  🚫 Rate Limited (429): {rate_limited} ({result.rate_limited_rate:.1f}%)")
    print(f"  ❌ Errors: {errors}")
    print(f"  ⏱️ Duration: {duration:.2f}s")
    print(f"  📊 Throughput: {GLOBAL_BURST/duration:.0f} req/s")
    
    # Validate
    if rate_limited >= 1000:  # Expect at least 1000 rejections
        print("  ✅ TEST PASSED: Global rate limiting working")
    else:
        print("  ⚠️ TEST WARNING: Expected more global rejections (may need longer window)")
    
    return result


async def test_rate_limit_recovery():
    """
    Test 3: Rate Limit Recovery
    Exhaust limit, wait, verify recovery.
    """
    print("\n" + "="*60)
    print("TEST 3: Rate Limit Recovery")
    print("="*60)
    
    test_ip = "192.168.100.1"
    
    async with aiohttp.ClientSession() as session:
        # Phase 1: Exhaust limit
        print("Phase 1: Exhausting rate limit...")
        for i in range(120):
            await send_request(session, create_event(i), custom_ip=test_ip)
        
        # Verify limited
        status = await send_request(session, create_event(999), custom_ip=test_ip)
        if status == 429:
            print("  ✅ Rate limit confirmed (429)")
        else:
            print(f"  ⚠️ Unexpected status: {status}")
        
        # Phase 2: Wait for recovery
        print("Phase 2: Waiting 12 seconds for token refill...")
        await asyncio.sleep(12)  # Wait for ~120 tokens to refill (10/sec * 12s)
        
        # Phase 3: Verify recovered
        status = await send_request(session, create_event(1000), custom_ip=test_ip)
        if status == 202:
            print("  ✅ Rate limit recovered (202)")
            print("  ✅ TEST PASSED: Recovery working correctly")
        else:
            print(f"  ❌ TEST FAILED: Expected 202, got {status}")


async def main():
    print("\n" + "="*60)
    print("CloudScale Rate Limiting Test Suite")
    print("="*60)
    print(f"Target: {API_URL}")
    print("="*60)
    
    # Run tests
    await test_single_ip_rate_limit()
    await test_global_rate_limit()
    await test_rate_limit_recovery()
    
    print("\n" + "="*60)
    print("All tests completed!")
    print("="*60)


if __name__ == "__main__":
    asyncio.run(main())
