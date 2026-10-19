import { test } from 'node:test'
import assert from 'node:assert/strict'
import { stableVersion, isNewerVersion, fetchLatestStableTag } from './appVersion.ts'

test('compares stable tags numerically and ignores equal or older versions', () => {
  assert.equal(isNewerVersion('v1.10.0', '1.9.9'), true)
  assert.equal(isNewerVersion('2.0.0', 'v1.99.99'), true)
  assert.equal(isNewerVersion('v1.0.1', '1.0.0'), true)
  assert.equal(isNewerVersion('v1.0.0', '1.0.0'), false)
  assert.equal(isNewerVersion('v0.9.0', '1.0.0'), false)
})

test('excludes prereleases, unrelated tags, and unknown installed versions', () => {
  for (const tag of ['backup-tag', 'v2.0.0-rc.1', '1.0', 'v01.0.0', 'dev']) {
    assert.equal(stableVersion(tag), null)
    assert.equal(isNewerVersion(tag, '1.0.0'), false)
  }
  assert.equal(isNewerVersion('v1.0.0', 'dev'), false)
  assert.equal(stableVersion('v1.2.3'), '1.2.3')
})

test('finds the highest stable tag across pages regardless of tag date order', async () => {
  const urls = []
  const latest = await fetchLatestStableTag(undefined, async url => {
    urls.push(url)
    return urls.length === 1
      ? Response.json([{ name: 'v1.9.0' }, { name: 'v3.0.0-beta.1' }, { name: 'backup-tag' }], {
        headers: { link: '<https://api.github.com/repos/rafael89/prumo/tags?per_page=100&page=2>; rel="next"' },
      })
      : Response.json([{ name: 'v1.10.0' }, { name: 'v1.0.0' }])
  })
  assert.equal(latest, 'v1.10.0')
  assert.equal(urls.length, 2)
  assert.match(urls[1], /page=2$/)
})

test('no stable tags produces no update', async () => {
  assert.equal(await fetchLatestStableTag(undefined, async () => Response.json([{ name: 'backup-tag' }])), null)
})

test('failed, rate limited, and malformed responses do not report a version', async () => {
  for (const response of [new Response(null, { status: 403 }), new Response(null, { status: 404 }), Response.json({})]) {
    await assert.rejects(fetchLatestStableTag(undefined, async () => response))
  }
  await assert.rejects(fetchLatestStableTag(undefined, async () => { throw new Error('offline') }))
})
