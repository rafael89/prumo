export function maskDecimalInput(value: string, fixed = false) {
  const input = value.trim()
  if (!input) return ''

  const separator = input.includes(',') ? ',' : input.includes('.') ? '.' : null
  const [rawInteger, rawFraction = ''] = separator ? input.split(separator, 2) : [input, '']
  const integer = rawInteger.replace(/\D/g, '').replace(/^0+(?=\d)/, '') || '0'
  const fraction = rawFraction.replace(/\D/g, '').slice(0, 2)

  if (!separator && !fixed) return integer
  if (!fixed && separator && fraction.length === 0) return `${integer},`
  return `${integer},${fraction.padEnd(2, '0')}`
}

export function parseDecimalInput(value: string) {
  return Number(value.replace(/\./g, '').replace(',', '.'))
}
