const runtimeApiBaseUrl = typeof window !== 'undefined' && window.__APP_CONFIG__ && window.__APP_CONFIG__.API_BASE_URL;
const API_BASE_URL = (runtimeApiBaseUrl || import.meta.env.VITE_API_BASE_URL || 'http://localhost:8080').replace(/\/$/, '');

async function request(path, options = {}) {
  const response = await fetch(`${API_BASE_URL}${path}`, options);
  const contentType = response.headers.get('content-type') || '';
  const isJson = contentType.includes('application/json');
  const body = isJson ? await response.json().catch(() => null) : await response.text().catch(() => '');

  if (!response.ok) {
    const message = (body && body.error) || (typeof body === 'string' && body) || `Request failed with status ${response.status}.`;
    const error = new Error(message);
    error.status = response.status;
    throw error;
  }

  return { status: response.status, body };
}

export async function createClass(payload) {
  const { body } = await request('/classes', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload)
  });
  return body;
}

export async function getClasses() {
  const { body } = await request('/classes');
  return body;
}

export async function createHold(classId, studentId) {
  const { body } = await request(`/classes/${classId}/holds`, {
    method: 'POST',
    headers: { 'X-Student-Id': studentId }
  });
  return body;
}

export async function getHold(holdId) {
  const { body } = await request(`/holds/${holdId}`);
  return body;
}

export async function confirmHold(holdId, studentId) {
  const { body } = await request(`/holds/${holdId}/confirm`, {
    method: 'POST',
    headers: { 'X-Student-Id': studentId }
  });
  return body;
}

export async function cancelHold(holdId, studentId) {
  await request(`/holds/${holdId}`, {
    method: 'DELETE',
    headers: { 'X-Student-Id': studentId }
  });
}

export async function getMyHolds(studentId) {
  const { body } = await request('/me/holds', {
    headers: { 'X-Student-Id': studentId }
  });
  return body;
}
