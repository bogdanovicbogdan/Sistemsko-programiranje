import concurrent.futures
import urllib.request
import urllib.error
import time
import re

TARGET_URL = "http://localhost:8080/?q=culture"
STATS_URL = "http://localhost:8080/stats"
CONCURRENT_REQUESTS = 200
TIMEOUT_SECONDS = 30


def fetch_url(url: str, timeout: int = TIMEOUT_SECONDS) -> tuple[int, float, str]:
    start = time.perf_counter()
    try:
        with urllib.request.urlopen(url, timeout=timeout) as response:
            body = response.read().decode("utf-8", errors="replace")
            elapsed = time.perf_counter() - start
            return response.status, elapsed, body
    except urllib.error.HTTPError as exc:
        elapsed = time.perf_counter() - start
        return exc.code, elapsed, exc.read().decode("utf-8", errors="replace")
    except Exception as exc:
        elapsed = time.perf_counter() - start
        return -1, elapsed, str(exc)


def parse_stampede_count(stats_body: str) -> int:
    match = re.search(r"Stampede sprecavanja:\s*(\d+)", stats_body)
    if match:
        return int(match.group(1))
    return -1


def main() -> None:
    print(f"Test stampeda: {CONCURRENT_REQUESTS} paralelnih zahteva na {TARGET_URL}")

    with concurrent.futures.ThreadPoolExecutor(max_workers=CONCURRENT_REQUESTS) as executor:
        futures = [executor.submit(fetch_url, TARGET_URL) for _ in range(CONCURRENT_REQUESTS)]

        results = []
        for future in concurrent.futures.as_completed(futures):
            status, elapsed, body = future.result()
            results.append((status, elapsed, body))
            print(f"Status={status} time={elapsed:.3f}s body_len={len(body)}")

    ok_count = sum(1 for status, _, _ in results if status == 200)
    error_count = len(results) - ok_count
    avg_time = sum(elapsed for _, elapsed, _ in results) / len(results)
    print()
    print(f"Ukupno zahteva: {len(results)}")
    print(f"200 OK: {ok_count}")
    print(f"Grešaka: {error_count}")
    print(f"Prosečno vreme: {avg_time:.3f}s")

    print("\nTražim statistiku keša... ")
    status, elapsed, stats_body = fetch_url(STATS_URL)
    print(f"Stats status={status} time={elapsed:.3f}s")
    print(stats_body)
    stampede_count = parse_stampede_count(stats_body)
    if stampede_count >= 0:
        print(f"Stampede sprečeno: {stampede_count}")
    else:
        print("Nije pronađena vrednost za stampede u odgovoru.")


if __name__ == "__main__":
    main()
