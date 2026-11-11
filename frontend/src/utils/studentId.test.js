import { describe, expect, it } from 'vitest'
import { isValidStudentId, normalizeStudentId } from './studentId'

describe('studentId utils', () => {
  it('normalizes to digits and max 17 chars', () => {
    expect(normalizeStudentId('2015-2691-234567890')).toBe('20152691234567890')
    expect(normalizeStudentId('abc123')).toBe('123')
    expect(normalizeStudentId('1234567890123456789')).toBe('12345678901234567')
  })

  it('validates exactly 17 digits', () => {
    expect(isValidStudentId('12345678901234567')).toBe(true)
    expect(isValidStudentId('1234567890123456')).toBe(false)
    expect(isValidStudentId('123456789012345678')).toBe(false)
    expect(isValidStudentId('1234567890123456A')).toBe(false)
    expect(isValidStudentId('')).toBe(false)
  })
})
