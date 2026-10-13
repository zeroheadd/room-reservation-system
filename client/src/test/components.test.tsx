import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ApiRequestError } from '../api/client';
import { ErrorAlert, StatusTag } from '../components/Feedback';

describe('ErrorAlert', () => {
  it('renders the rule code for a single rule violation', () => {
    render(
      <ErrorAlert
        error={{
          code: 'VALIDATION_FAILED',
          message: 'Room is inactive and cannot be booked.',
          errors: [{ code: 'R7_ROOM_INACTIVE', message: 'Room is inactive and cannot be booked.' }]
        }}
      />
    );

    expect(screen.getByRole('alert')).toHaveTextContent('R7_ROOM_INACTIVE');
    expect(screen.getByRole('alert')).toHaveTextContent('inactive');
  });

  it('lists every broken rule when several are reported', () => {
    render(
      <ErrorAlert
        error={{
          code: 'VALIDATION_FAILED',
          message: 'The reservation breaks 2 booking rules.',
          errors: [
            { code: 'R5_CAPACITY_EXCEEDED', message: 'Room seats 6 people but the meeting has 20 attendees.' },
            { code: 'R6_BOOKING_HORIZON_EXCEEDED', message: 'A reservation cannot start more than 90 days from now.' }
          ]
        }}
      />
    );

    expect(screen.getByRole('alert')).toHaveTextContent('R5_CAPACITY_EXCEEDED');
    expect(screen.getByRole('alert')).toHaveTextContent('R6_BOOKING_HORIZON_EXCEEDED');
    expect(screen.getAllByRole('listitem')).toHaveLength(2);
  });

  it('renders nothing when there is no error', () => {
    const { container } = render(<ErrorAlert error={null} />);
    expect(container).toBeEmptyDOMElement();
  });
});

describe('StatusTag', () => {
  it('maps a status to its tag class', () => {
    render(<StatusTag status="Cancelled" />);
    expect(screen.getByText('Cancelled')).toHaveClass('tag-cancelled');
  });
});

describe('ApiRequestError', () => {
  it('exposes rule messages and the violation flag', () => {
    const error = new ApiRequestError(400, {
      code: 'VALIDATION_FAILED',
      message: 'The reservation breaks 1 booking rule.',
      errors: [{ code: 'R2_ROOM_OVERLAP', message: 'Room is already booked.' }]
    });

    expect(error.isRuleViolation).toBe(true);
    expect(error.ruleMessages).toEqual(['Room is already booked.']);
    expect(error.message).toBe('The reservation breaks 1 booking rule.');
  });

  it('is not a rule violation for a 404', () => {
    const error = new ApiRequestError(404, {
      code: 'RESERVATION_NOT_FOUND',
      message: 'Reservation 42 does not exist.',
      errors: []
    });

    expect(error.isRuleViolation).toBe(false);
    expect(error.ruleMessages).toEqual([]);
  });
});

describe('api client', () => {
  it('sends room filters as query parameters and surfaces the error envelope', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 400,
      json: async () => ({
        code: 'VALIDATION_FAILED',
        message: "Unknown equipment 'Hologram'.",
        errors: []
      })
    } as unknown as Response);

    vi.stubGlobal('fetch', fetchMock);

    const { api } = await import('../api/client');

    await expect(api.getRooms({ minCapacity: 10, equipment: ['Projector', 'Whiteboard'], floor: '3rd' }))
      .rejects.toBeInstanceOf(ApiRequestError);

    const calledUrl = String(fetchMock.mock.calls[0][0]);
    expect(calledUrl).toContain('minCapacity=10');
    expect(calledUrl).toContain('equipment=Projector%2CWhiteboard');
    expect(calledUrl).toContain('floor=3rd');

    vi.unstubAllGlobals();
  });
});
