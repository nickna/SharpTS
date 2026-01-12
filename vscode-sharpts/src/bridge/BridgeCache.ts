/**
 * TTL-based cache for bridge responses.
 */

interface CacheEntry<T> {
    value: T;
    timestamp: number;
}

export class BridgeCache {
    private cache = new Map<string, CacheEntry<unknown>>();
    private attributeListCache: CacheEntry<AttributeInfo[]> | null = null;

    // Default TTLs in milliseconds
    private readonly TYPE_RESOLVE_TTL = 5 * 60 * 1000;      // 5 minutes
    private readonly ATTRIBUTE_LIST_TTL = 10 * 60 * 1000;   // 10 minutes
    private readonly ATTRIBUTE_INFO_TTL = 5 * 60 * 1000;    // 5 minutes

    get<T>(key: string): T | undefined {
        const entry = this.cache.get(key);
        if (!entry) return undefined;

        const ttl = this.getTtl(key);
        if (Date.now() - entry.timestamp > ttl) {
            this.cache.delete(key);
            return undefined;
        }

        return entry.value as T;
    }

    set<T>(key: string, value: T): void {
        this.cache.set(key, { value, timestamp: Date.now() });
    }

    getAttributeList(): AttributeInfo[] | undefined {
        if (!this.attributeListCache) return undefined;
        if (Date.now() - this.attributeListCache.timestamp > this.ATTRIBUTE_LIST_TTL) {
            this.attributeListCache = null;
            return undefined;
        }
        return this.attributeListCache.value;
    }

    setAttributeList(list: AttributeInfo[]): void {
        this.attributeListCache = { value: list, timestamp: Date.now() };
    }

    invalidate(): void {
        this.cache.clear();
        this.attributeListCache = null;
    }

    private getTtl(key: string): number {
        if (key.startsWith('resolve:')) return this.TYPE_RESOLVE_TTL;
        if (key.startsWith('info:')) return this.ATTRIBUTE_INFO_TTL;
        return this.TYPE_RESOLVE_TTL;
    }
}

interface AttributeInfo {
    name: string;
    fullName: string;
    namespace: string;
    assembly: string;
}
