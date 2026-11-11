import { useEffect, useMemo, useState } from 'react'
import { cancelHold, confirmHold, createClass, createHold, getClasses, getMyHolds } from './api/client'
import { isValidStudentId, normalizeStudentId } from './utils/studentId'

const STUDENT_ID_KEY = 'studentId'

function formatDate(value) {
  if (!value) return '-'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return String(value)
  return date.toLocaleString()
}

function effectiveStatus(hold, now) {
  if (!hold) return ''
  if (hold.status === 'HELD' && hold.expiresAt) {
    const expiry = new Date(hold.expiresAt).getTime()
    if (!Number.isNaN(expiry) && expiry <= now) return 'EXPIRED'
  }
  return hold.status
}

function formatRemaining(expiresAt, now) {
  const expiry = new Date(expiresAt).getTime()
  if (Number.isNaN(expiry)) return '-'
  const diff = expiry - now
  if (diff <= 0) return 'Expired'
  const totalSeconds = Math.floor(diff / 1000)
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  return `${minutes}m ${String(seconds).padStart(2, '0')}s`
}

function friendlyError(err, fallback) {
  if (!err) return fallback
  if (err.message && err.message.includes('Failed to fetch')) {
    return 'Unable to reach the server. Please check your connection and try again.'
  }
  return err.message || fallback
}

export default function App() {
  const [studentId, setStudentId] = useState(() => normalizeStudentId(localStorage.getItem(STUDENT_ID_KEY) || ''))
  const [className, setClassName] = useState('')
  const [totalSeats, setTotalSeats] = useState('')
  const [classes, setClasses] = useState([])
  const [myHolds, setMyHolds] = useState([])
  const [selectedHoldId, setSelectedHoldId] = useState('')
  const [now, setNow] = useState(Date.now())
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState('')

  useEffect(() => {
    localStorage.setItem(STUDENT_ID_KEY, studentId)
  }, [studentId])

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [])

  async function refreshClasses(showError = false) {
    try {
      const data = await getClasses()
      setClasses(Array.isArray(data) ? data : [])
    } catch (err) {
      if (showError) setError(friendlyError(err, 'Could not load classes.'))
    }
  }

  async function refreshMyHolds(showError = false) {
    const sid = studentId.trim()
    if (!isValidStudentId(sid)) {
      setMyHolds([])
      return
    }
    if (showError) setBusy('refresh-holds')
    try {
      const data = await getMyHolds(sid)
      setMyHolds(Array.isArray(data) ? data : [])
      if (showError) setError('')
    } catch (err) {
      if (showError) setError(friendlyError(err, 'Could not load your applications.'))
    } finally {
      if (showError) setBusy((b) => (b === 'refresh-holds' ? '' : b))
    }
  }

  useEffect(() => {
    refreshClasses(true)
    const timer = setInterval(() => refreshClasses(false), 3000)
    return () => clearInterval(timer)
  }, [])

  useEffect(() => {
    refreshMyHolds(false)
    if (!isValidStudentId(studentId.trim())) return
    const timer = setInterval(() => refreshMyHolds(false), 3000)
    return () => clearInterval(timer)
  }, [studentId])

  const myHoldsWithStatus = useMemo(
    () => myHolds.map((h) => ({ ...h, effectiveStatus: effectiveStatus(h, now) })),
    [myHolds, now]
  )

  const selectedHold = useMemo(() => {
    if (selectedHoldId) {
      const found = myHoldsWithStatus.find((h) => h.holdId === selectedHoldId)
      if (found) return found
    }
    return myHoldsWithStatus[0] || null
  }, [myHoldsWithStatus, selectedHoldId])

  useEffect(() => {
    if (!selectedHoldId && myHoldsWithStatus.length > 0) {
      setSelectedHoldId(myHoldsWithStatus[0].holdId)
    }
    if (selectedHoldId && !myHoldsWithStatus.some((h) => h.holdId === selectedHoldId)) {
      setSelectedHoldId(myHoldsWithStatus[0]?.holdId || '')
    }
  }, [myHoldsWithStatus, selectedHoldId])

  async function onCreateClass(e) {
    e.preventDefault()
    setError('')
    setNotice('')
    if (!className.trim()) {
      setError('Please enter a class name.')
      return
    }
    const seats = Number(totalSeats)
    if (!Number.isInteger(seats) || seats <= 0) {
      setError('Total seats must be a positive whole number.')
      return
    }
    setBusy('create-class')
    try {
      await createClass({ name: className.trim(), totalSeats: seats })
      setClassName('')
      setTotalSeats('')
      setNotice('Class created successfully.')
      await refreshClasses(true)
    } catch (err) {
      setError(friendlyError(err, 'Could not create class.'))
    } finally {
      setBusy('')
    }
  }

  async function onApply(classId) {
    setError('')
    setNotice('')
    const sid = studentId.trim()
    if (!isValidStudentId(sid)) {
      setError('Enter a valid 17-digit birth registration number (digits only).')
      return
    }
    setBusy(`apply-${classId}`)
    try {
      const hold = await createHold(classId, sid)
      setSelectedHoldId(hold.holdId)
      setNotice('Application submitted. Your seat is held until the timer expires.')
      await Promise.all([refreshClasses(true), refreshMyHolds(true)])
    } catch (err) {
      setError(friendlyError(err, 'Could not submit application.'))
      await refreshClasses(false)
    } finally {
      setBusy('')
    }
  }

  async function onConfirm(holdId) {
    setError('')
    setNotice('')
    const sid = studentId.trim()
    if (!isValidStudentId(sid)) {
      setError('Enter a valid 17-digit birth registration number (digits only).')
      return
    }
    setBusy(`confirm-${holdId}`)
    try {
      await confirmHold(holdId, sid)
      setNotice('Admission fee submitted and admission confirmed.')
      await Promise.all([refreshClasses(true), refreshMyHolds(true)])
    } catch (err) {
      setError(friendlyError(err, 'Could not confirm application.'))
      await Promise.all([refreshClasses(false), refreshMyHolds(false)])
    } finally {
      setBusy('')
    }
  }

  async function onCancel(holdId) {
    setError('')
    setNotice('')
    const sid = studentId.trim()
    if (!isValidStudentId(sid)) {
      setError('Enter a valid 17-digit birth registration number (digits only).')
      return
    }
    setBusy(`cancel-${holdId}`)
    try {
      await cancelHold(holdId, sid)
      setNotice('Application cancelled.')
      await Promise.all([refreshClasses(true), refreshMyHolds(true)])
    } catch (err) {
      setError(friendlyError(err, 'Could not cancel application.'))
      await Promise.all([refreshClasses(false), refreshMyHolds(false)])
    } finally {
      setBusy('')
    }
  }

  return (
    <div className="container">
      <h1>School Admission System</h1>
      <p className="muted">Admissions are first-come, first-served. Class seat counts and application status update automatically.</p>

      {error ? <div className="error" role="alert">{error}</div> : null}
      {notice ? <div className="success">{notice}</div> : null}

      <section className="card">
        <h2>Student Identifier</h2>
        <div className="row">
          <div className="field" style={{ minWidth: 320 }}>
            <label htmlFor="studentId">Birth Registration Number (17 digits)</label>
            <input
              id="studentId"
              type="text"
              value={studentId}
              onChange={(e) => setStudentId(normalizeStudentId(e.target.value))}
              placeholder="Enter 17-digit birth registration number" maxLength={17} inputMode="numeric" pattern="\d{17}" autoComplete="off"
            />
          </div>
        </div>
        <p className="muted">This identifier is used as the X-Student-Id header for applications and is saved in this browser. It must be exactly 17 digits.</p>
      </section>

      <section className="card">
        <h2>Create Class (School Admin)</h2>
        <form className="row" onSubmit={onCreateClass}>
          <div className="field">
            <label htmlFor="className">Class Name</label>
            <input id="className" value={className} onChange={(e) => setClassName(e.target.value)} placeholder="Class 6 - Morning Shift" />
          </div>
          <div className="field">
            <label htmlFor="totalSeats">Total Seats</label>
            <input id="totalSeats" type="number" min="1" value={totalSeats} onChange={(e) => setTotalSeats(e.target.value)} placeholder="120" />
          </div>
          <button type="submit" disabled={busy === 'create-class'}>
            {busy === 'create-class' ? 'Creating...' : 'Create Class'}
          </button>
        </form>
      </section>

      <section className="card">
        <h2>Class List</h2>
        {classes.length === 0 ? (
          <p className="muted">No classes created yet.</p>
        ) : (
          <table>
            <thead>
              <tr>
                <th>Class</th>
                <th>Total</th>
                <th>Held</th>
                <th>Confirmed</th>
                <th>Available</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              {classes.map((cls) => (
                <tr key={cls.id}>
                  <td>{cls.name}</td>
                  <td>{cls.totalSeats}</td>
                  <td>{cls.heldSeats}</td>
                  <td>{cls.confirmedSeats}</td>
                  <td>{cls.availableSeats}</td>
                  <td>
                    <button onClick={() => onApply(cls.id)} disabled={busy === `apply-${cls.id}`}>
                      {busy === `apply-${cls.id}` ? 'Applying...' : 'Apply'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <section className="card">
        <h2>Application</h2>
        {!selectedHold ? (
          <p className="muted">Select or create an application to view its details.</p>
        ) : (
          <div>
            <p><strong>Hold ID:</strong> {selectedHold.holdId}</p>
            <p><strong>Class:</strong> {selectedHold.className || selectedHold.classId}</p>
            <p><strong>Status:</strong> <span className="badge">{selectedHold.effectiveStatus}</span></p>
            <p><strong>Expires At:</strong> {formatDate(selectedHold.expiresAt)}</p>
            <p><strong>Time Remaining:</strong> {selectedHold.effectiveStatus === 'HELD' ? formatRemaining(selectedHold.expiresAt, now) : '-'}</p>
            <div className="actions">
              <button
                onClick={() => onConfirm(selectedHold.holdId)}
                disabled={selectedHold.effectiveStatus !== 'HELD' || busy === `confirm-${selectedHold.holdId}`}
              >
                {busy === `confirm-${selectedHold.holdId}` ? 'Processing...' : 'Pay Admission Fee'}
              </button>
              <button
                onClick={() => onCancel(selectedHold.holdId)}
                disabled={selectedHold.effectiveStatus !== 'HELD' || busy === `cancel-${selectedHold.holdId}`}
              >
                {busy === `cancel-${selectedHold.holdId}` ? 'Cancelling...' : 'Cancel'}
              </button>
            </div>
            {selectedHold.effectiveStatus === 'EXPIRED' ? (
              <p className="muted">This application has expired. The seat has been released.</p>
            ) : null}
          </div>
        )}
      </section>

      <section className="card">
        <h2>My Applications</h2>
        <div className="actions" style={{ marginBottom: 12 }}>
          <button onClick={() => refreshMyHolds(true)} disabled={!isValidStudentId(studentId.trim()) || busy === 'refresh-holds'}>
            {busy === 'refresh-holds' ? 'Refreshing...' : 'Refresh Applications'}
          </button>
        </div>
        {!isValidStudentId(studentId.trim()) ? (
          <p className="muted">Enter a valid 17-digit birth registration number to view applications.</p>
        ) : myHoldsWithStatus.length === 0 ? (
          <p className="muted">No applications found for this student.</p>
        ) : (
          <table>
            <thead>
              <tr>
                <th>Hold ID</th>
                <th>Class</th>
                <th>Status</th>
                <th>Expires At</th>
                <th>Time Remaining</th>
                <th>View</th>
              </tr>
            </thead>
            <tbody>
              {myHoldsWithStatus.map((hold) => (
                <tr key={hold.holdId}>
                  <td>{hold.holdId}</td>
                  <td>{hold.className || hold.classId}</td>
                  <td><span className="badge">{hold.effectiveStatus}</span></td>
                  <td>{formatDate(hold.expiresAt)}</td>
                  <td>{hold.effectiveStatus === 'HELD' ? formatRemaining(hold.expiresAt, now) : '-'}</td>
                  <td>
                    <button onClick={() => setSelectedHoldId(hold.holdId)}>View</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  )
}
