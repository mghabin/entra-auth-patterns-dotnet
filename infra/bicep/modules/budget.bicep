metadata name = 'budget'
metadata description = 'RG-scoped monthly cost budget with Forecasted+Actual notifications. Defense against runaway spend.'

extension az

@description('Budget name (resource-scoped, must be unique within the RG).')
param name string

@description('Monthly amount in subscription currency (USD by default).')
@minValue(1)
@maxValue(100000)
param monthlyAmount int

@description('Email addresses notified on threshold breach. Empty = no email channel.')
param contactEmails array = []

@description('Start of the budget window (yyyy-MM-01 UTC). Must be the first of a month.')
param startDate string

resource budget 'Microsoft.Consumption/budgets@2024-08-01' = {
  name: name
  properties: {
    category:  'Cost'
    amount:    monthlyAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: empty(contactEmails) ? {} : {
      actualAt80: {
        enabled:        true
        operator:       'GreaterThan'
        threshold:      80
        thresholdType:  'Actual'
        contactEmails:  contactEmails
      }
      actualAt100: {
        enabled:        true
        operator:       'GreaterThanOrEqualTo'
        threshold:      100
        thresholdType:  'Actual'
        contactEmails:  contactEmails
      }
      forecastAt100: {
        enabled:        true
        operator:       'GreaterThan'
        threshold:      100
        thresholdType:  'Forecasted'
        contactEmails:  contactEmails
      }
    }
  }
}

@description('Resource ID of the budget.')
output budgetId string = budget.id
