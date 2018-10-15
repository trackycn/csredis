﻿//using SafeObjectPool;
//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;

//namespace CSRedis {
//	partial class CSRedisClient {

//		async Task<T> GetConnectionAndExecuteAsync<T>(RedisClientPool pool, Func<Object<RedisClient>, Task<T>> handle) {
//			Object<RedisClient> obj = null;
//			Exception ex = null;
//			try {
//				obj = await pool.GetAsync();
//				try {
//					return await handle(obj);
//				} catch (Exception ex2) {
//					ex = ex2;
//					throw ex;
//				}
//			} finally {
//				pool.Return(obj, ex);
//			}
//		}

//		/// <summary>
//		/// 缓存壳
//		/// </summary>
//		/// <typeparam name="T">缓存类型</typeparam>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="timeoutSeconds">缓存秒数</param>
//		/// <param name="getDataAsync">获取源数据的函数</param>
//		/// <returns></returns>
//		async public Task<T> CacheShellAsync<T>(string key, int timeoutSeconds, Func<Task<T>> getDataAsync) {
//			if (timeoutSeconds <= 0) return await getDataAsync();
//			var cacheValue = await GetAsync(key);
//			if (cacheValue != null) {
//				try {
//					return (T)this.Deserialize(cacheValue, typeof(T));
//				} catch {
//					await RemoveAsync(key);
//					throw;
//				}
//			}
//			var ret = await getDataAsync();
//			await SetAsync(key, this.Serialize(ret), timeoutSeconds);
//			return ret;
//		}
//		/// <summary>
//		/// 缓存壳(哈希表)
//		/// </summary>
//		/// <typeparam name="T">缓存类型</typeparam>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="field">字段</param>
//		/// <param name="timeoutSeconds">缓存秒数</param>
//		/// <param name="getDataAsync">获取源数据的函数</param>
//		/// <returns></returns>
//		async public Task<T> CacheShellAsync<T>(string key, string field, int timeoutSeconds, Func<Task<T>> getDataAsync) {
//			if (timeoutSeconds <= 0) return await getDataAsync();
//			var cacheValue = await HashGetAsync(key, field);
//			if (cacheValue != null) {
//				try {
//					var value = ((T, long))this.Deserialize(cacheValue, typeof((T, long)));
//					if (DateTime.Now.Subtract(_dt1970.AddSeconds(value.Item2)).TotalSeconds <= timeoutSeconds) return value.Item1;
//				} catch {
//					await HashDeleteAsync(key, field);
//					throw;
//				}
//			}
//			var ret = await getDataAsync();
//			await HashSetAsync(key, field, this.Serialize((ret, (long)DateTime.Now.Subtract(_dt1970).TotalSeconds)));
//			return ret;
//		}
//		/// <summary>
//		/// 缓存壳(哈希表)，将 fields 每个元素存储到单独的缓存片，实现最大化复用
//		/// </summary>
//		/// <typeparam name="T">缓存类型</typeparam>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="fields">字段</param>
//		/// <param name="timeoutSeconds">缓存秒数</param>
//		/// <param name="getDataAsync">获取源数据的函数，输入参数是没有缓存的 fields，返回值应该是 (field, value)[]</param>
//		/// <returns></returns>
//		async public Task<T[]> CacheShellAsync<T>(string key, string[] fields, int timeoutSeconds, Func<string[], Task<(string, T)[]>> getDataAsync) {
//			fields = fields?.Distinct().ToArray();
//			if (fields == null || fields.Length == 0) return new T[0];
//			if (timeoutSeconds <= 0) return (await getDataAsync(fields)).Select(a => a.Item2).ToArray();

//			var ret = new T[fields.Length];
//			var cacheValue = await HashMGetAsync(key, fields);
//			var fieldsMGet = new Dictionary<string, int>();

//			for (var a = 0; a < cacheValue.Length; a++) {
//				if (cacheValue[a] != null) {
//					try {
//						var value = ((T, long))this.Deserialize(cacheValue[a], typeof((T, long)));
//						if (DateTime.Now.Subtract(_dt1970.AddSeconds(value.Item2)).TotalSeconds <= timeoutSeconds) {
//							ret[a] = value.Item1;
//							continue;
//						}
//					} catch {
//						await HashDeleteAsync(key, fields[a]);
//						throw;
//					}
//				}
//				fieldsMGet.Add(fields[a], a);
//			}

//			if (fieldsMGet.Any()) {
//				var getDataIntput = fieldsMGet.Keys.ToArray();
//				var data = await getDataAsync(getDataIntput);
//				var mset = new object[fieldsMGet.Count * 2];
//				var msetIndex = 0;
//				foreach (var d in data) {
//					if (fieldsMGet.ContainsKey(d.Item1) == false) throw new Exception($"使用 CacheShell 请确认 getData 返回值 (string, T)[] 中的 Item1 值: {d.Item1} 存在于 输入参数: {string.Join(",", getDataIntput)}");
//					ret[fieldsMGet[d.Item1]] = d.Item2;
//					mset[msetIndex++] = d.Item1;
//					mset[msetIndex++] = this.Serialize((d.Item2, (long)DateTime.Now.Subtract(_dt1970).TotalSeconds));
//					fieldsMGet.Remove(d.Item1);
//				}
//				foreach (var fieldNull in fieldsMGet.Keys) {
//					ret[fieldsMGet[fieldNull]] = default(T);
//					mset[msetIndex++] = fieldNull;
//					mset[msetIndex++] = this.Serialize((default(T), (long)DateTime.Now.Subtract(_dt1970).TotalSeconds));
//				}
//				if (mset.Any()) await HashSetAsync(key, mset);
//			}
//			return ret.ToArray();
//		}

//		#region 分区方式 Execute
//		private Task<T> ExecuteScalarAsync<T>(string key, Func<Object<RedisClient>, string, Task<T>> hander) {
//			if (key == null) return Task.FromResult(default(T));
//			var pool = NodeRule == null || Nodes.Count == 1 ? Nodes.First().Value : (Nodes.TryGetValue(NodeRule(key), out var b) ? b : Nodes.First().Value);
//			key = string.Concat(pool.Prefix, key);
//			return GetConnectionAndExecuteAsync(pool, conn => hander(conn, key));
//		}
//		async private Task<T[]> ExeucteArrayAsync<T>(string[] key, Func<Object<RedisClient>, string[], Task<T[]>> hander) {
//			if (key == null || key.Any() == false) return new T[0];
//			if (NodeRule == null || Nodes.Count == 1) {
//				var pool = Nodes.First().Value;
//				var keys = key.Select(a => string.Concat(pool.Prefix, a)).ToArray();
//				return await GetConnectionAndExecuteAsync(pool, conn => hander(conn, keys));
//			}
//			var rules = new Dictionary<string, List<(string, int)>>();
//			for (var a = 0; a < key.Length; a++) {
//				var rule = NodeRule(key[a]);
//				if (rules.ContainsKey(rule)) rules[rule].Add((key[a], a));
//				else rules.Add(rule, new List<(string, int)> { (key[a], a) });
//			}
//			T[] ret = new T[key.Length];
//			foreach (var r in rules) {
//				var pool = Nodes.TryGetValue(r.Key, out var b) ? b : Nodes.First().Value;
//				var keys = r.Value.Select(a => string.Concat(pool.Prefix, a.Item1)).ToArray();
//				await GetConnectionAndExecuteAsync(pool, async conn => {
//					var vals = await hander(conn, keys);
//					for (var z = 0; z < r.Value.Count; z++) {
//						ret[r.Value[z].Item2] = vals == null || z >= vals.Length ? default(T) : vals[z];
//					}
//					return 0;
//				});
//			}
//			return ret;
//		}
//		async private Task<long> ExecuteNonQueryAsync(string[] key, Func<Object<RedisClient>, string[], Task<long>> hander) {
//			if (key == null || key.Any() == false) return 0;
//			if (NodeRule == null || Nodes.Count == 1) {
//				var pool = Nodes.First().Value;
//				var keys = key.Select(a => string.Concat(pool.Prefix, a)).ToArray();
//				return await GetConnectionAndExecuteAsync(pool, conn => hander(conn, keys));
//			}
//			var rules = new Dictionary<string, List<string>>();
//			for (var a = 0; a < key.Length; a++) {
//				var rule = NodeRule(key[a]);
//				if (rules.ContainsKey(rule)) rules[rule].Add(key[a]);
//				else rules.Add(rule, new List<string> { key[a] });
//			}
//			long affrows = 0;
//			foreach (var r in rules) {
//				var pool = Nodes.TryGetValue(r.Key, out var b) ? b : Nodes.First().Value;
//				var keys = r.Value.Select(a => string.Concat(pool.Prefix, a)).ToArray();
//				affrows += await GetConnectionAndExecuteAsync(pool, conn => hander(conn, keys));
//			}
//			return affrows;
//		}
//		#endregion

//		/// <summary>
//		/// 设置指定 key 的值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">字符串值</param>
//		/// <param name="expireSeconds">过期(秒单位)</param>
//		/// <param name="exists">Nx, Xx</param>
//		/// <returns></returns>
//		async public Task<bool> SetAsync(string key, string value, int expireSeconds = -1, CSRedisExistence? exists = null) => await ExecuteScalarAsync(key, (c, k) => expireSeconds > 0 || exists != null ? c.Value.SetAsync(k, value, expireSeconds > 0 ? new int?(expireSeconds) : null, exists == CSRedisExistence.Nx ? new RedisExistence?(RedisExistence.Nx) : (exists == CSRedisExistence.Xx ? new RedisExistence?(RedisExistence.Xx) : null)) : c.Value.SetAsync(k, value)) == "OK";
//		/// <summary>
//		/// 设置指定 key 的值(字节流)
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">字节流</param>
//		/// <param name="expireSeconds">过期(秒单位)</param>
//		/// <param name="exists">Nx, Xx</param>
//		/// <returns></returns>
//		async public Task<bool> SetBytesAsync(string key, byte[] value, int expireSeconds = -1, CSRedisExistence? exists = null) => await ExecuteScalarAsync(key, (c, k) => expireSeconds > 0 || exists != null ? c.Value.SetAsync(k, value, expireSeconds > 0 ? new int?(expireSeconds) : null, exists == CSRedisExistence.Nx ? new RedisExistence?(RedisExistence.Nx) : (exists == CSRedisExistence.Xx ? new RedisExistence?(RedisExistence.Xx) : null)) : c.Value.SetAsync(k, value)) == "OK";
//		/// <summary>
//		/// 只有在 key 不存在时设置 key 的值。
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">字符串值</param>
//		/// <returns></returns>
//		public Task<bool> SetNxAsync(string key, string value) => ExecuteScalarAsync(key, (c, k) => c.Value.SetNxAsync(k, value));
//		/// <summary>
//		/// 同时设置一个或多个 key-value 对。
//		/// </summary>
//		/// <param name="keyValues">key1 value1 [key2 value2]</param>
//		/// <returns></returns>
//		public Task<bool> MSetAsync(params string[] keyValues) => MSetPrivateAsync(CSRedisExistence.Xx, keyValues);
//		/// <summary>
//		/// 同时设置一个或多个 key-value 对，当且仅当所有给定 key 都不存在。警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="keyValues">key1 value1 [key2 value2]</param>
//		/// <returns></returns>
//		public Task<bool> MSetNxAsync(params string[] keyValues) => MSetPrivateAsync(CSRedisExistence.Nx, keyValues);
//		async private Task<bool> MSetPrivateAsync(CSRedisExistence exists, params string[] keyValues) {
//			if (keyValues == null || keyValues.Any() == false) return false;
//			if (keyValues.Length % 2 != 0) throw new Exception("keyValues 参数是键值对，不应该出现奇数(数量)，请检查使用姿势。");
//			var dic = new Dictionary<string, string>();
//			for (var a = 0; a < keyValues.Length; a += 2) {
//				if (dic.ContainsKey(keyValues[a])) dic[keyValues[a]] = dic[keyValues[a + 1]];
//				else dic.Add(keyValues[a], keyValues[a + 1]);
//			}
//			Func<Object<RedisClient>, string[], Task<long>> handle = async (c, k) => {
//				var prefix = (c.Pool as RedisClientPool)?.Prefix;
//				var parms = new string[k.Length * 2];
//				for (var a = 0; a < k.Length; a++) {
//					parms[a * 2] = k[a];
//					parms[a * 2 + 1] = dic[string.IsNullOrEmpty(prefix) ? k[a] : k[a].Substring(prefix.Length)];
//				}
//				if (exists == CSRedisExistence.Nx) return await c.Value.MSetNxAsync(parms) ? 1 : 0;
//				return await c.Value.MSetAsync(parms) == "OK" ? 1 : 0;
//			};
//			if (exists == CSRedisExistence.Nx) return await NodesNotSupportAsync(dic.Keys.ToArray(), 0, handle) > 0;
//			return await ExecuteNonQueryAsync(dic.Keys.ToArray(), handle) > 0;
//		}
//		/// <summary>
//		/// 获取指定 key 的值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<string> GetAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.GetAsync(k));
//		/// <summary>
//		/// 获取多个指定 key 的值(数组)
//		/// </summary>
//		/// <param name="keys">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<string[]> MGetAsync(params string[] keys) => ExeucteArrayAsync(keys, (c, k) => c.Value.MGetAsync(k));
//		/// <summary>
//		/// 获取多个指定 key 的值(数组)
//		/// </summary>
//		/// <param name="keys">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<string[]> GetStringsAsync(params string[] keys) => ExeucteArrayAsync(keys, (c, k) => c.Value.MGetAsync(k));
//		/// <summary>
//		/// 获取指定 key 的值(字节流)
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<byte[]> GetBytesAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.GetBytesAsync(k));
//		/// <summary>
//		/// 用于在 key 存在时删除 key
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<long> RemoveAsync(params string[] key) => ExecuteNonQueryAsync(key, (c, k) => c.Value.DelAsync(k));
//		/// <summary>
//		/// 检查给定 key 是否存在
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<bool> ExistsAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.ExistsAsync(k));
//		/// <summary>
//		/// 将 key 所储存的值加上给定的增量值（increment）
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">增量值(默认=1)</param>
//		/// <returns></returns>
//		public Task<long> IncrementAsync(string key, long value = 1) => ExecuteScalarAsync(key, (c, k) => c.Value.IncrByAsync(k, value));
//		/// <summary>
//		/// 为给定 key 设置过期时间
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="expire">过期时间</param>
//		/// <returns></returns>
//		public Task<bool> ExpireAsync(string key, TimeSpan expire) => ExecuteScalarAsync(key, (c, k) => c.Value.ExpireAsync(k, expire));
//		/// <summary>
//		/// 以秒为单位，返回给定 key 的剩余生存时间
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<long> TtlAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.TtlAsync(k));
//		/// <summary>
//		/// 执行脚本
//		/// </summary>
//		/// <param name="script">脚本</param>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="args">参数</param>
//		/// <returns></returns>
//		public Task<object> EvalAsync(string script, string key, params object[] args) => ExecuteScalarAsync(key, (c, k) => c.Value.EvalAsync(script, new[] { k }, args));
//		/// <summary>
//		/// 查找所有分区中符合给定模式(pattern)的 key
//		/// </summary>
//		/// <param name="pattern">如：runoob*</param>
//		/// <returns></returns>
//		async public Task<string[]> KeysAsync(string pattern) {
//			List<string> ret = new List<string>();
//			foreach (var pool in Nodes)
//				ret.AddRange(await GetConnectionAndExecuteAsync(pool.Value, conn => conn.Value.KeysAsync(pattern)));
//			return ret.ToArray();
//		}
//		/// <summary>
//		/// Redis Publish 命令用于将信息发送到指定群集节点的频道
//		/// </summary>
//		/// <param name="channel">频道名</param>
//		/// <param name="data">消息文本</param>
//		/// <returns></returns>
//		async public Task<long> PublishAsync(string channel, string data) {
//			var msgid = await HashIncrementAsync("CSRedisPublishMsgId", channel, 1);
//			return await ExecuteScalarAsync(channel, (c, k) => c.Value.PublishAsync(channel, $"{msgid}|{data}"));
//		}

//		#region Hash 操作
//		/// <summary>
//		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中，value 可以是 string 或 byte[]
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="keyValues">field1 value1 [field2 value2]</param>
//		/// <returns></returns>
//		public Task<string> HashSetAsync(string key, params object[] keyValues) => HashSetExpireAsync(key, TimeSpan.Zero, keyValues);
//		/// <summary>
//		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中，value 可以是 string 或 byte[]
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="expire">过期时间</param>
//		/// <param name="keyValues">field1 value1 [field2 value2]</param>
//		/// <returns></returns>
//		async public Task<string> HashSetExpireAsync(string key, TimeSpan expire, params object[] keyValues) {
//			if (keyValues == null || keyValues.Any() == false) return null;
//			if (keyValues.Length % 2 != 0) throw new Exception("keyValues 参数是键值对，不应该出现奇数(数量)，请检查使用姿势。");
//			if (expire > TimeSpan.Zero) {
//				var lua = "ARGV[1] = redis.call('HMSET', KEYS[1]";
//				var argv = new List<object>();
//				for (int a = 0, argvIdx = 3; a < keyValues.Length; a += 2, argvIdx++) {
//					lua += ", '" + (keyValues[a]?.ToString().Replace("'", "\\'")) + "', ARGV[" + argvIdx + "]";
//					argv.Add(keyValues[a + 1]);
//				}
//				lua += @") redis.call('EXPIRE', KEYS[1], ARGV[2]) return ARGV[1]";
//				argv.InsertRange(0, new object[] { "", (long)expire.TotalSeconds });
//				return (await EvalAsync(lua, key, argv.ToArray()))?.ToString();
//			}
//			return await ExecuteScalarAsync(key, (c, k) => c.Value.HMSetAsync(k, keyValues));
//		}
//		/// <summary>
//		/// 只有在字段 field 不存在时，设置哈希表字段的值。
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="field">字段</param>
//		/// <param name="value">值(string 或 byte[])</param>
//		/// <returns></returns>
//		public Task<bool> HashSetNxAsync(string key, string field, object value) => ExecuteScalar(key, (c, k) => c.Value.HSetNxAsync(k, field, value));
//		/// <summary>
//		/// 获取存储在哈希表中指定字段的值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="field">字段</param>
//		/// <returns></returns>
//		public Task<string> HashGetAsync(string key, string field) => ExecuteScalarAsync(key, (c, k) => c.Value.HGetAsync(k, field));
//		/// <summary>
//		/// 获取存储在哈希表中指定字段的值，返回 byte[]
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="field">字段</param>
//		/// <returns>byte[]</returns>
//		public Task<byte[]> HashGetBytesAsync(string key, string field) => ExecuteScalarAsync(key, (c, k) => c.Value.HGetBytesAsync(k, field));
//		/// <summary>
//		/// 获取存储在哈希表中多个字段的值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="fields">字段</param>
//		/// <returns></returns>
//		public Task<string[]> HashMGetAsync(string key, params string[] fields) => ExecuteScalarAsync(key, (c, k) => c.Value.HMGetAsync(k, fields));
//		/// <summary>
//		/// 获取存储在哈希表中多个字段的值，每个 field 的值类型返回 byte[]
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="fields">字段</param>
//		/// <returns>byte[][]</returns>
//		public Task<byte[][]> HashMGetBytesAsync(string key, params string[] fields) => ExecuteScalarAsync(key, (c, k) => c.Value.HMGetBytesAsync(k, fields));
//		/// <summary>
//		/// 为哈希表 key 中的指定字段的整数值加上增量 increment
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="field">字段</param>
//		/// <param name="value">增量值(默认=1)</param>
//		/// <returns></returns>
//		public Task<long> HashIncrementAsync(string key, string field, long value = 1) => ExecuteScalarAsync(key, (c, k) => c.Value.HIncrByAsync(k, field, value));
//		/// <summary>
//		/// 为哈希表 key 中的指定字段的整数值加上增量 increment
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="field">字段</param>
//		/// <param name="value">增量值(默认=1)</param>
//		/// <returns></returns>
//		public Task<double> HashIncrementFloatAsync(string key, string field, double value = 1) => ExecuteScalarAsync(key, (c, k) => c.Value.HIncrByFloatAsync(k, field, value));
//		/// <summary>
//		/// 删除一个或多个哈希表字段
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="fields">字段</param>
//		/// <returns></returns>
//		async public Task<long> HashDeleteAsync(string key, params string[] fields) => fields == null || fields.Any() == false ? 0 : await ExecuteScalarAsync(key, (c, k) => c.Value.HDelAsync(k, fields));
//		/// <summary>
//		/// 查看哈希表 key 中，指定的字段是否存在
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="field">字段</param>
//		/// <returns></returns>
//		public Task<bool> HashExistsAsync(string key, string field) => ExecuteScalarAsync(key, (c, k) => c.Value.HExistsAsync(k, field));
//		/// <summary>
//		/// 获取哈希表中字段的数量
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<long> HashLengthAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.HLenAsync(k));
//		/// <summary>
//		/// 获取在哈希表中指定 key 的所有字段和值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<Dictionary<string, string>> HashGetAllAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.HGetAllAsync(k));
//		/// <summary>
//		/// 获取所有哈希表中的字段
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<string[]> HashKeysAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.HKeysAsync(k));
//		/// <summary>
//		/// 获取哈希表中所有值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<string[]> HashValsAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.HValsAsync(k));
//		#endregion

//		#region List 操作
//		/// <summary>
//		/// 它是 LPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BLPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="timeout">超时(秒)</param>
//		/// <param name="keys">一个或多个列表，不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<string> BLPopAsync(int timeout, params string[] keys) => NodesNotSupportAsync(keys, null, (c, k) => c.Value.BLPopAsync(timeout, k));
//		/// <summary>
//		/// 它是 LPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BLPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="timeout">超时(秒)</param>
//		/// <param name="keys">一个或多个列表，不含prefix前辍</param>
//		/// <returns></returns>
//		async public Task<(string key, string value)?> BLPopWithKeyAsync(int timeout, params string[] keys) {
//			string[] rkeys = null;
//			var tuple = await NodesNotSupportAsync(keys, null, (c, k) => c.Value.BLPopWithKeyAsync(timeout, rkeys = k));
//			if (tuple == null) return null;
//			var key = tuple.Item1;
//			for (var a = 0; a < rkeys.Length; a++)
//				if (rkeys[a] == tuple.Item1) {
//					key = keys[a];
//					break;
//				}
//			return (key, tuple.Item2);
//		}
//		/// <summary>
//		/// 它是 RPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BRPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="timeout">超时(秒)</param>
//		/// <param name="keys">一个或多个列表，不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<string> BRPopAsync(int timeout, params string[] keys) => NodesNotSupportAsync(keys, null, (c, k) => c.Value.BRPopAsync(timeout, k));
//		/// <summary>
//		/// 它是 RPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BRPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="timeout">超时(秒)</param>
//		/// <param name="keys">一个或多个列表，不含prefix前辍</param>
//		/// <returns></returns>
//		async public Task<(string key, string value)?> BRPopWithKeyAsync(int timeout, params string[] keys) {
//			string[] rkeys = null;
//			var tuple = await NodesNotSupportAsync(keys, null, (c, k) => c.Value.BRPopWithKeyAsync(timeout, rkeys = k));
//			if (tuple == null) return null;
//			var key = tuple.Item1;
//			for (var a = 0; a < rkeys.Length; a++)
//				if (rkeys[a] == tuple.Item1) {
//					key = keys[a];
//					break;
//				}
//			return (key, tuple.Item2);
//		}
//		/// <summary>
//		/// 通过索引获取列表中的元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="index">索引</param>
//		/// <returns></returns>
//		public Task<string> LIndexAsync(string key, long index) => ExecuteScalarAsync(key, (c, k) => c.Value.LIndexAsync(k, index));
//		/// <summary>
//		/// 在列表的元素前面插入元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="pivot">列表的元素</param>
//		/// <param name="value">新元素</param>
//		/// <returns></returns>
//		public Task<long> LInsertBeforeAsync(string key, string pivot, string value) => ExecuteScalarAsync(key, (c, k) => c.Value.LInsertAsync(k, RedisInsert.Before, pivot, value));
//		/// <summary>
//		/// 在列表的元素后面插入元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="pivot">列表的元素</param>
//		/// <param name="value">新元素</param>
//		/// <returns></returns>
//		public Task<long> LInsertAfterAsync(string key, string pivot, string value) => ExecuteScalarAsync(key, (c, k) => c.Value.LInsertAsync(k, RedisInsert.After, pivot, value));
//		/// <summary>
//		/// 获取列表长度
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<long> LLenAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.LLenAsync(k));
//		/// <summary>
//		/// 移出并获取列表的第一个元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<string> LPopAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.LPopAsync(k));
//		/// <summary>
//		/// 移除并获取列表最后一个元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<string> RPopAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.RPopAsync(k));
//		/// <summary>
//		/// 将一个或多个值插入到列表头部
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">一个或多个值</param>
//		/// <returns></returns>
//		async public Task<long> LPushAsync(string key, params string[] value) => value == null || value.Any() == false ? 0 : await ExecuteScalarAsync(key, (c, k) => c.Value.LPushAsync(k, value));
//		/// <summary>
//		/// 在列表中添加一个或多个值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">一个或多个值</param>
//		/// <returns></returns>
//		async public Task<long> RPushAsync(string key, params string[] value) => value == null || value.Any() == false ? 0 : await ExecuteScalarAsync(key, (c, k) => c.Value.RPushAsync(k, value));
//		/// <summary>
//		/// 获取列表指定范围内的元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <returns></returns>
//		public Task<string[]> LRangAsync(string key, long start, long stop) => ExecuteScalarAsync(key, (c, k) => c.Value.LRangeAsync(k, start, stop));
//		/// <summary>
//		/// 根据参数 count 的值，移除列表中与参数 value 相等的元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="count">移除的数量，大于0时从表头删除数量count，小于0时从表尾删除数量-count，等于0移除所有</param>
//		/// <param name="value">元素</param>
//		/// <returns></returns>
//		public Task<long> LRemAsync(string key, long count, string value) => ExecuteScalarAsync(key, (c, k) => c.Value.LRemAsync(k, count, value));
//		/// <summary>
//		/// 通过索引设置列表元素的值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="index">索引</param>
//		/// <param name="value">值</param>
//		/// <returns></returns>
//		async public Task<bool> LSetAsync(string key, long index, string value) => await ExecuteScalarAsync(key, (c, k) => c.Value.LSetAsync(k, index, value)) == "OK";
//		/// <summary>
//		/// 对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <returns></returns>
//		async public Task<bool> LTrimAsync(string key, long start, long stop) => await ExecuteScalarAsync(key, (c, k) => c.Value.LTrimAsync(k, start, stop)) == "OK";
//		#endregion

//		#region Set 操作
//		/// <summary>
//		/// 向集合添加一个或多个成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="members">一个或多个成员</param>
//		/// <returns></returns>
//		async public Task<long> SAddAsync(string key, params string[] members) {
//			if (members == null || members.Any() == false) return 0;
//			return await ExecuteScalarAsync(key, (c, k) => c.Value.SAddAsync(k, members));
//		}
//		/// <summary>
//		/// 获取集合的成员数
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<long> SCardAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.SCardAsync(k));
//		/// <summary>
//		/// 返回给定所有集合的差集，警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="keys">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<string[]> SDiffAsync(params string[] keys) => NodesNotSupportAsync(keys, new string[0], (c, k) => c.Value.SDiffAsync(k));
//		/// <summary>
//		/// 返回给定所有集合的差集并存储在 destination 中，警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="destination">新的无序集合，不含prefix前辍</param>
//		/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<long> SDiffStoreAsync(string destination, params string[] keys) => NodesNotSupportAsync(new[] { destination }.Concat(keys).ToArray(), 0, (c, k) => c.Value.SDiffStoreAsync(k.First(), k.Where((ki, kj) => kj > 0).ToArray()));
//		/// <summary>
//		/// 返回给定所有集合的交集，警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="keys">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<string[]> SInterAsync(params string[] keys) => NodesNotSupportAsync(keys, new string[0], (c, k) => c.Value.SInterAsync(k));
//		/// <summary>
//		/// 返回给定所有集合的交集并存储在 destination 中，警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="destination">新的无序集合，不含prefix前辍</param>
//		/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<long> SInterStoreAsync(string destination, params string[] keys) => NodesNotSupportAsync(new[] { destination }.Concat(keys).ToArray(), 0, (c, k) => c.Value.SInterStoreAsync(k.First(), k.Where((ki, kj) => kj > 0).ToArray()));
//		/// <summary>
//		/// 返回集合中的所有成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<string[]> SMembersAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.SMembersAsync(k));
//		/// <summary>
//		/// 将 member 元素从 source 集合移动到 destination 集合
//		/// </summary>
//		/// <param name="source">无序集合key，不含prefix前辍</param>
//		/// <param name="destination">目标无序集合key，不含prefix前辍</param>
//		/// <param name="member">成员</param>
//		/// <returns></returns>
//		async public Task<bool> SMoveAsync(string source, string destination, string member) {
//			string rule = string.Empty;
//			if (Nodes.Count > 1) {
//				var rule1 = NodeRule(source);
//				var rule2 = NodeRule(destination);
//				if (rule1 != rule2) {
//					if (await SRemAsync(source, member) <= 0) return false;
//					return await SAddAsync(destination, member) > 0;
//				}
//				rule = rule1;
//			}
//			var pool = Nodes.TryGetValue(rule, out var b) ? b : Nodes.First().Value;
//			var key1 = string.Concat(pool.Prefix, source);
//			var key2 = string.Concat(pool.Prefix, destination);
//			return await GetConnectionAndExecuteAsync(pool, conn => conn.Value.SMoveAsync(key1, key2, member));
//		}
//		/// <summary>
//		/// 移除并返回集合中的一个随机元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<string> SPopAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.SPopAsync(k));
//		/// <summary>
//		/// 返回集合中一个或多个随机数的元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="count">返回个数</param>
//		/// <returns></returns>
//		public Task<string[]> SRandMemberAsync(string key, int count = 1) => ExecuteScalarAsync(key, (c, k) => c.Value.SRandMemberAsync(k, count));
//		/// <summary>
//		/// 移除集合中一个或多个成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="members">一个或多个成员</param>
//		/// <returns></returns>
//		async public Task<long> SRemAsync(string key, params string[] members) {
//			if (members == null || members.Any() == false) return 0;
//			return await ExecuteScalarAsync(key, (c, k) => c.Value.SRemAsync(k, members));
//		}
//		/// <summary>
//		/// 返回所有给定集合的并集，警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="keys">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<string[]> SUnionAsync(params string[] keys) => NodesNotSupportAsync(keys, new string[0], (c, k) => c.Value.SUnionAsync(k));
//		/// <summary>
//		/// 所有给定集合的并集存储在 destination 集合中，警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="destination">新的无序集合，不含prefix前辍</param>
//		/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<long> SUnionStoreAsync(string destination, params string[] keys) => NodesNotSupportAsync(new[] { destination }.Concat(keys).ToArray(), 0, (c, k) => c.Value.SUnionStoreAsync(k.First(), k.Where((ki, kj) => kj > 0).ToArray()));
//		#endregion

//		async private Task<T> NodesNotSupportAsync<T>(string[] keys, T defaultValue, Func<Object<RedisClient>, string[], Task<T>> callbackAsync) {
//			if (keys == null || keys.Any() == false) return defaultValue;
//			var rules = Nodes.Count > 1 ? keys.Select(a => NodeRule(a)).Distinct() : new[] { Nodes.FirstOrDefault().Key };
//			if (rules.Count() > 1) throw new Exception("由于开启了群集模式，keys 分散在多个节点，无法使用此功能");
//			var pool = Nodes.TryGetValue(rules.First(), out var b) ? b : Nodes.First().Value;
//			string[] rkeys = new string[keys.Length];
//			for (int a = 0; a < keys.Length; a++) rkeys[a] = string.Concat(pool.Prefix, keys[a]);
//			if (rkeys.Length == 0) return defaultValue;
//			return await GetConnectionAndExecuteAsync(pool, conn => callbackAsync(conn, rkeys));
//		}

//		#region Sorted Set 操作
//		/// <summary>
//		/// 向有序集合添加一个或多个成员，或者更新已存在成员的分数
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="scoreMembers">一个或多个成员分数</param>
//		/// <returns></returns>
//		async public Task<long> ZAddAsync(string key, params (double, string)[] scoreMembers) {
//			if (scoreMembers == null || scoreMembers.Any() == false) return 0;
//			var ms = scoreMembers.Select(a => new Tuple<double, string>(a.Item1, a.Item2)).ToArray();
//			return await ExecuteScalarAsync(key, (c, k) => c.Value.ZAddAsync<double, string>(k, ms));
//		}
//		/// <summary>
//		/// 获取有序集合的成员数量
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<long> ZCardAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.ZCardAsync(k));
//		/// <summary>
//		/// 计算在有序集合中指定区间分数的成员数量
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="min">分数最小值</param>
//		/// <param name="max">分数最大值</param>
//		/// <returns></returns>
//		public Task<long> ZCountAsync(string key, double min, double max) => ExecuteScalarAsync(key, (c, k) => c.Value.ZCountAsync(k, min, max));
//		/// <summary>
//		/// 有序集合中对指定成员的分数加上增量 increment
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="memeber">成员</param>
//		/// <param name="increment">增量值(默认=1)</param>
//		/// <returns></returns>
//		public Task<double> ZIncrByAsync(string key, string memeber, double increment = 1) => ExecuteScalarAsync(key, (c, k) => c.Value.ZIncrByAsync(k, increment, memeber));

//		#region 多个有序集合 交集
//		/// <summary>
//		/// 计算给定的一个或多个有序集的最大值交集，将结果集存储在新的有序集合 destination 中，警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="destination">新的有序集合，不含prefix前辍</param>
//		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<long> ZInterStoreMaxAsync(string destination, params string[] keys) => ZInterStoreAsync(destination, RedisAggregate.Max, keys);
//		/// <summary>
//		/// 计算给定的一个或多个有序集的最小值交集，将结果集存储在新的有序集合 destination 中，警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="destination">新的有序集合，不含prefix前辍</param>
//		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<long> ZInterStoreMinAsync(string destination, params string[] keys) => ZInterStoreAsync(destination, RedisAggregate.Min, keys);
//		/// <summary>
//		/// 计算给定的一个或多个有序集的合值交集，将结果集存储在新的有序集合 destination 中，警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="destination">新的有序集合，不含prefix前辍</param>
//		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<long> ZInterStoreSumAsync(string destination, params string[] keys) => ZInterStoreAsync(destination, RedisAggregate.Sum, keys);
//		private Task<long> ZInterStoreAsync(string destination, RedisAggregate aggregate, params string[] keys) => NodesNotSupportAsync(new[] { destination }.Concat(keys).ToArray(), 0, (c, k) => c.Value.ZInterStoreAsync(k.First(), null, aggregate, k.Where((ki, kj) => kj > 0).ToArray()));
//		#endregion

//		#region 多个有序集合 并集
//		/// <summary>
//		/// 计算给定的一个或多个有序集的最大值并集，将该并集(结果集)储存到 destination，警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="destination">新的有序集合，不含prefix前辍</param>
//		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<long> ZUnionStoreMaxAsync(string destination, params string[] keys) => ZUnionStoreAsync(destination, RedisAggregate.Max, keys);
//		/// <summary>
//		/// 计算给定的一个或多个有序集的最小值并集，将该并集(结果集)储存到 destination，警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="destination">新的有序集合，不含prefix前辍</param>
//		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<long> ZUnionStoreMinAsync(string destination, params string[] keys) => ZUnionStoreAsync(destination, RedisAggregate.Min, keys);
//		/// <summary>
//		/// 计算给定的一个或多个有序集的合值并集，将该并集(结果集)储存到 destination，警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="destination">新的有序集合，不含prefix前辍</param>
//		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
//		/// <returns></returns>
//		public Task<long> ZUnionStoreSumAsync(string destination, params string[] keys) => ZUnionStoreAsync(destination, RedisAggregate.Sum, keys);
//		private Task<long> ZUnionStoreAsync(string destination, RedisAggregate aggregate, params string[] keys) => NodesNotSupportAsync(new[] { destination }.Concat(keys).ToArray(), 0, (c, k) => c.Value.ZUnionStoreAsync(k.First(), null, aggregate, k.Where((ki, kj) => kj > 0).ToArray()));
//		#endregion

//		/// <summary>
//		/// 通过索引区间返回有序集合成指定区间内的成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <returns></returns>
//		public Task<string[]> ZRangeAsync(string key, long start, long stop) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeAsync(k, start, stop, false));
//		/// <summary>
//		/// 通过分数返回有序集合指定区间内的成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="min">最小分数</param>
//		/// <param name="max">最大分数</param>
//		/// <param name="limit">返回多少成员</param>
//		/// <param name="offset">返回条件偏移位置</param>
//		/// <returns></returns>
//		public Task<string[]> ZRangeByScoreAsync(string key, double min, double max, long? limit = null, long offset = 0) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeByScoreAsync(k, min, max, false, false, false, offset, limit));
//		/// <summary>
//		/// 通过分数返回有序集合指定区间内的成员和分数
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="min">最小分数</param>
//		/// <param name="max">最大分数</param>
//		/// <param name="limit">返回多少成员</param>
//		/// <param name="offset">返回条件偏移位置</param>
//		/// <returns></returns>
//		async public Task<(string member, double score)[]> ZRangeByScoreWithScoresAsync(string key, double min, double max, long? limit = null, long offset = 0) {
//			var res = await ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeByScoreAsync(k, min, max, true, false, false, offset, limit));
//			var ret = new List<(string member, double score)>();
//			if (res != null && res.Length % 2 == 0)
//				for (var a = 0; a < res.Length; a += 2)
//					ret.Add((res[a], double.TryParse(res[a + 1], out var tryd) ? tryd : 0));
//			return ret.ToArray();
//		}
//		/// <summary>
//		/// 返回有序集合中指定成员的索引
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="member">成员</param>
//		/// <returns></returns>
//		public Task<long?> ZRankAsync(string key, string member) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRankAsync(k, member));
//		/// <summary>
//		/// 移除有序集合中的一个或多个成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="member">一个或多个成员</param>
//		/// <returns></returns>
//		public Task<long> ZRemAsync(string key, params string[] member) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRemAsync(k, member));
//		/// <summary>
//		/// 移除有序集合中给定的排名区间的所有成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <returns></returns>
//		public Task<long> ZRemRangeByRankAsync(string key, long start, long stop) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRemRangeByRankAsync(k, start, stop));
//		/// <summary>
//		/// 移除有序集合中给定的分数区间的所有成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="min">最小分数</param>
//		/// <param name="max">最大分数</param>
//		/// <returns></returns>
//		public Task<long> ZRemRangeByScoreAsync(string key, double min, double max) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRemRangeByScoreAsync(k, min, max));
//		/// <summary>
//		/// 返回有序集中指定区间内的成员，通过索引，分数从高到底
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <returns></returns>
//		public Task<string[]> ZRevRangeAsync(string key, long start, long stop) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRangeAsync(k, start, stop, false));
//		/// <summary>
//		/// 返回有序集中指定分数区间内的成员，分数从高到低排序
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="min">最小分数</param>
//		/// <param name="max">最大分数</param>
//		/// <param name="limit">返回多少成员</param>
//		/// <param name="offset">返回条件偏移位置</param>
//		/// <returns></returns>
//		public Task<string[]> ZRevRangeByScoreAsync(string key, double max, double min, long? limit = null, long? offset = 0) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRangeByScoreAsync(k, max, min, false, false, false, offset, limit));
//		/// <summary>
//		/// 返回有序集中指定分数区间内的成员和分数，分数从高到低排序
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="min">最小分数</param>
//		/// <param name="max">最大分数</param>
//		/// <param name="limit">返回多少成员</param>
//		/// <param name="offset">返回条件偏移位置</param>
//		/// <returns></returns>
//		async public Task<(string member, double score)[]> ZRevRangeByScoreWithScoresAsync(string key, double max, double min, long? limit = null, long offset = 0) {
//			var res = await ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRangeByScoreAsync(k, max, min, true, false, false, offset, limit));
//			var ret = new List<(string member, double score)>();
//			if (res != null && res.Length % 2 == 0)
//				for (var a = 0; a < res.Length; a += 2)
//					ret.Add((res[a], double.TryParse(res[a + 1], out var tryd) ? tryd : 0));
//			return ret.ToArray();
//		}
//		/// <summary>
//		/// 返回有序集合中指定成员的排名，有序集成员按分数值递减(从大到小)排序
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="member">成员</param>
//		/// <returns></returns>
//		public Task<long?> ZRevRankAsync(string key, string member) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRankAsync(k, member));
//		/// <summary>
//		/// 返回有序集中，成员的分数值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="member">成员</param>
//		/// <returns></returns>
//		public Task<double?> ZScoreAsync(string key, string member) => ExecuteScalarAsync(key, (c, k) => c.Value.ZScoreAsync(k, member));
//		#endregion
//	}
//}