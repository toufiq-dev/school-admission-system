export function normalizeStudentId(value) {
  return (value || '').replace(/\D/g, '').slice(0, 17)
}

export function isValidStudentId(value) {
  return /^\d{17}$/.test(value || '')
}
